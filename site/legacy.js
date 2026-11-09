let runtime = null;

async function load() {
  const { dotnet } = await import("./legacy/wwwroot/_framework/dotnet.js");
  const { getAssemblyExports, getConfig } = await dotnet.create();
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  return exports.LegacyImport.Program;
}

export async function migrate(xml) {
  runtime ??= load();
  return (await runtime).Migrate(xml);
}
