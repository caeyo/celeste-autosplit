use std::{
    collections::BTreeMap,
    env, fs,
    io::{self, Read, Write},
    net::{TcpListener, TcpStream},
    path::{Path, PathBuf},
    process::{Command, Stdio},
    sync::{
        Arc, Mutex,
        mpsc::{Sender, channel},
    },
    thread,
    time::Duration,
};

use livesplit_core::{StoredAutoSplitterSettings, run::parser::composite};

use crate::HARNESS_DIR;

type Outputs = Arc<Mutex<BTreeMap<String, Vec<u8>>>>;

fn repo() -> PathBuf {
    Path::new(HARNESS_DIR).parent().unwrap().to_owned()
}

fn content_type(path: &Path) -> &'static str {
    match path.extension().and_then(|extension| extension.to_str()) {
        Some("html") => "text/html; charset=utf-8",
        Some("js") => "text/javascript; charset=utf-8",
        Some("wasm") => "application/wasm",
        Some("json") => "application/json; charset=utf-8",
        _ => "application/octet-stream",
    }
}

fn respond(
    mut stream: &TcpStream,
    status: &str,
    body: &[u8],
    content_type: &str,
) -> io::Result<()> {
    write!(
        stream,
        "HTTP/1.1 {status}\r\nContent-Type: {content_type}\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",
        body.len()
    )?;
    stream.write_all(body)
}

fn handle(mut stream: TcpStream, outputs: &Outputs, report: &Sender<String>) -> io::Result<()> {
    let mut received = Vec::new();
    let mut buffer = [0; 8192];
    let head_end = loop {
        if let Some(end) = received.windows(4).position(|window| window == b"\r\n\r\n") {
            break end + 4;
        }
        let read = stream.read(&mut buffer)?;
        if read == 0 {
            return Ok(());
        }
        received.extend_from_slice(&buffer[..read]);
    };
    let head = String::from_utf8_lossy(&received[..head_end]).into_owned();
    let mut parts = head.split_whitespace();
    let method = parts.next().unwrap_or_default().to_owned();
    let path = parts.next().unwrap_or_default().to_owned();
    let path = path.split('?').next().unwrap_or_default().to_owned();
    if method == "POST" {
        let length = head
            .lines()
            .find_map(|line| {
                line.to_ascii_lowercase()
                    .strip_prefix("content-length:")
                    .map(|value| value.trim().parse().unwrap_or(0))
            })
            .unwrap_or(0);
        while received.len() < head_end + length {
            let read = stream.read(&mut buffer)?;
            if read == 0 {
                break;
            }
            received.extend_from_slice(&buffer[..read]);
        }
        let body = received[head_end..].to_vec();
        if path == "/report" {
            report
                .send(String::from_utf8_lossy(&body).into_owned())
                .unwrap();
        } else if let Some(name) = path.strip_prefix("/output/") {
            outputs.lock().unwrap().insert(name.to_owned(), body);
        }
        return respond(&stream, "200 OK", b"", "text/plain");
    }
    let file = repo().join(path.trim_start_matches('/'));
    match fs::read(&file) {
        Ok(body) => respond(&stream, "200 OK", &body, content_type(&file)),
        Err(_) => respond(&stream, "404 Not Found", b"not found", "text/plain"),
    }
}

fn serve(outputs: Outputs, report: Sender<String>) -> u16 {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    thread::spawn(move || {
        for stream in listener.incoming().flatten() {
            let outputs = outputs.clone();
            let report = report.clone();
            thread::spawn(move || {
                let _ = handle(stream, &outputs, &report);
            });
        }
    });
    port
}

fn legacy_names() -> Vec<String> {
    let dir = repo().join("component/CelesteAutosplit.Tests/LegacySplits");
    let mut names: Vec<_> = fs::read_dir(dir)
        .unwrap()
        .map(|entry| entry.unwrap().path())
        .filter(|path| path.extension().is_some_and(|extension| extension == "lss"))
        .map(|path| path.file_stem().unwrap().to_string_lossy().into_owned())
        .collect();
    names.sort();
    names
}

fn normalize(xml: &str) -> String {
    let xml = xml.lines().map(str::trim).collect::<String>();
    let xml = xml
        .strip_prefix("<AutoSplitterSettings>")
        .and_then(|xml| xml.strip_suffix("</AutoSplitterSettings>"))
        .map(str::to_owned)
        .unwrap_or(xml);
    xml.replace(" />", "/>")
}

fn between<'a>(text: &'a str, start: &str, end: &str) -> &'a str {
    let from = text.find(start).unwrap();
    let to = text.rfind(end).unwrap() + end.len();
    &text[from..to]
}

fn read_settings(output: &[u8]) -> String {
    let text = std::str::from_utf8(output).unwrap();
    let settings = if text.contains("<Segments>") {
        composite::parse(output, None)
            .unwrap_or_else(|e| panic!("{e:?}: {:?}", &text[..text.len().min(300)]))
            .run
            .stored_auto_splitter_settings()
            .unwrap()
    } else {
        let xml = between(text, "<AutoSplitterSettings>", "</AutoSplitterSettings>");
        let inner = xml
            .strip_prefix("<AutoSplitterSettings>")
            .and_then(|xml| xml.strip_suffix("</AutoSplitterSettings>"))
            .unwrap();
        StoredAutoSplitterSettings::parse(inner).unwrap()
    };
    normalize(&settings.to_xml_string().replace("\"></Setting>", "\"/>"))
}

fn assert_livesplit_core_reads(output: &[u8], expected: &str, what: &str) {
    assert_eq!(read_settings(output), normalize(expected), "{what}");
}

#[test]
#[ignore = "needs the CHROME environment variable; site/test/run.sh sets it"]
fn the_site_writes_what_livesplit_core_reads() {
    let chrome = env::var("CHROME").expect("CHROME, the path of chrome-headless-shell");
    let repo = repo();
    fs::copy(
        repo.join("schema/settings-schema.json"),
        repo.join("site/settings-schema.json"),
    )
    .unwrap();
    let legacy = legacy_names();

    let outputs = Outputs::default();
    let (report_sender, report_receiver) = channel();
    let port = serve(outputs.clone(), report_sender);
    let url = format!(
        "http://127.0.0.1:{port}/site/test/index.html?legacy={}",
        legacy.join(",")
    );

    let profile = env::temp_dir().join(format!("celeste-autosplit-site-{port}"));
    let mut browser = Command::new(chrome)
        .args([
            "--no-sandbox",
            "--disable-gpu",
            "--disable-dev-shm-usage",
            "--remote-debugging-port=0",
        ])
        .arg(format!("--user-data-dir={}", profile.display()))
        .arg(url)
        .stdout(Stdio::null())
        .stderr(Stdio::inherit())
        .spawn()
        .unwrap();

    let report = report_receiver.recv_timeout(Duration::from_secs(300));
    let _ = browser.kill();
    let _ = browser.wait();
    let failures: Vec<String> = serde_json::from_str(&report.expect("the page's report")).unwrap();
    assert!(failures.is_empty(), "{}", failures.join("\n"));

    let outputs = outputs.lock().unwrap();
    for name in ["edit", "insert", "compact", "utf16", "comment"] {
        let cases = repo.join("site/test/cases");
        let output = outputs
            .get(&format!("edit/{name}"))
            .unwrap_or_else(|| panic!("no output for {name}"));
        let expected = fs::read(cases.join(format!("{name}.expected.lss"))).unwrap();
        assert!(
            output == &expected,
            "{name}: the site's file differs from the hand-written one:\n{}\n---\n{}",
            String::from_utf8_lossy(output),
            String::from_utf8_lossy(&expected),
        );
        if ["utf16", "comment"].contains(&name) {
            continue;
        }
        let expected = String::from_utf8(expected).unwrap();
        assert_livesplit_core_reads(
            output,
            between(
                &expected,
                "<AutoSplitterSettings>",
                "</AutoSplitterSettings>",
            ),
            name,
        );
    }
    for name in &legacy {
        let output = outputs
            .get(&format!("legacy/{name}"))
            .unwrap_or_else(|| panic!("no output for {name}"));
        let expected = fs::read_to_string(repo.join(format!(
            "component/CelesteAutosplit.Tests/LegacySplits/{name}.expected.xml"
        )))
        .unwrap();
        assert_livesplit_core_reads(output, &expected, name);
    }
}
