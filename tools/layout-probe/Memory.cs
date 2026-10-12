using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace LayoutProbe
{
    /// <summary>
    /// Raw addresses of managed objects and fields in this process. The IL here is
    /// unverifiable, which dynamic methods may be in a full-trust process.
    /// </summary>
    internal static class Memory
    {
        private static readonly Func<object, IntPtr> addressOf = BuildAddressOf();

        /// <summary>The object's current address. The GC may move it afterwards.</summary>
        public static ulong AddressOf(object obj) => ToAddress(addressOf(obj));

        /// <summary>
        /// Offset of an instance field from its object's address. Marshal.OffsetOf only
        /// works for sequential and explicit layouts, and these classes use auto layout.
        /// </summary>
        public static long FieldOffset(FieldInfo field, object instance)
        {
            var method = new DynamicMethod("OffsetOf_" + field.Name, typeof(IntPtr), new[] { typeof(object) }, typeof(Memory).Module, true);
            ILGenerator il = method.GetILGenerator();
            // &obj.field - obj, both in one method so the GC can't move the object in between.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, field);
            il.Emit(OpCodes.Conv_U);
            il.Emit(OpCodes.Ldarga_S, (byte)0);
            il.Emit(OpCodes.Ldind_I);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Ret);
            var offsetOf = (Func<object, IntPtr>)method.CreateDelegate(typeof(Func<object, IntPtr>));
            return offsetOf(instance).ToInt64();
        }

        /// <summary>Address of a static field's storage, which never moves.</summary>
        public static ulong StaticFieldAddress(FieldInfo field)
        {
            var method = new DynamicMethod("AddressOf_" + field.Name, typeof(IntPtr), Type.EmptyTypes, typeof(Memory).Module, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldsflda, field);
            il.Emit(OpCodes.Conv_U);
            il.Emit(OpCodes.Ret);
            return ToAddress(((Func<IntPtr>)method.CreateDelegate(typeof(Func<IntPtr>)))());
        }

        private static Func<object, IntPtr> BuildAddressOf()
        {
            var method = new DynamicMethod("AddressOf", typeof(IntPtr), new[] { typeof(object) }, typeof(Memory).Module, true);
            ILGenerator il = method.GetILGenerator();
            // Reads the argument's reference as a plain pointer.
            il.Emit(OpCodes.Ldarga_S, (byte)0);
            il.Emit(OpCodes.Ldind_I);
            il.Emit(OpCodes.Ret);
            return (Func<object, IntPtr>)method.CreateDelegate(typeof(Func<object, IntPtr>));
        }

        public static ulong ToAddress(IntPtr pointer) =>
            IntPtr.Size == 4 ? (uint)pointer.ToInt32() : (ulong)pointer.ToInt64();

        public static IntPtr ToPointer(ulong address) =>
            IntPtr.Size == 4 ? new IntPtr(unchecked((int)(uint)address)) : new IntPtr(unchecked((long)address));

        public static ulong ReadPointer(ulong address) => ToAddress(Marshal.ReadIntPtr(ToPointer(address)));

        public static long ReadInt64(ulong address) => Marshal.ReadInt64(ToPointer(address));

        public static int ReadInt32(ulong address) => Marshal.ReadInt32(ToPointer(address));

        public static byte ReadByte(ulong address) => Marshal.ReadByte(ToPointer(address));

        public static char ReadChar(ulong address) => (char)Marshal.ReadInt16(ToPointer(address));

        /// <summary>
        /// Reads up to <paramref name="max"/> bytes, stopping at the first unmapped page:
        /// reading one would crash the process instead of throwing.
        /// </summary>
        public static byte[] ReadMapped(ulong address, int max)
        {
            ulong pageSize = (ulong)Environment.SystemPageSize;
            ulong end = address;
            while (end < address + (ulong)max)
            {
                ulong page = end & ~(pageSize - 1);
                if (!IsMapped(page, pageSize))
                {
                    break;
                }
                end = page + pageSize;
            }
            var bytes = new byte[Math.Min((ulong)max, end - address)];
            Marshal.Copy(ToPointer(address), bytes, 0, bytes.Length);
            return bytes;
        }

        private static bool IsMapped(ulong page, ulong pageSize)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var size = new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryBasicInformation)));
                if (VirtualQuery(ToPointer(page), out MemoryBasicInformation info, size) == UIntPtr.Zero)
                {
                    return false;
                }
                return info.State == MemCommit && (info.Protect & (PageNoAccess | PageGuard)) == 0;
            }

            // msync fails with ENOMEM on unmapped pages on both Linux and macOS (mincore
            // doesn't on macOS). MS_ASYNC is 1 on both and does nothing to anonymous memory.
            return msync(ToPointer(page), new UIntPtr(pageSize), 1) == 0;
        }

        private const uint MemCommit = 0x1000;
        private const uint PageNoAccess = 0x01;
        private const uint PageGuard = 0x100;

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryBasicInformation
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll")]
        private static extern UIntPtr VirtualQuery(IntPtr address, out MemoryBasicInformation info, UIntPtr length);

        [DllImport("libc")]
        private static extern int msync(IntPtr address, UIntPtr length, int flags);
    }
}
