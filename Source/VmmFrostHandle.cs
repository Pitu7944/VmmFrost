using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using VmmFrost.ScatterAPI;
using VmmFrost.Vmmsharp;

namespace VmmFrost
{
    /// <summary>
    /// Wraps common Memory Reading/Writing functions on a Vmm Handle.
    /// </summary>
    public sealed class VmmFrostHandle : IDisposable
    {
        /// <summary>
        /// Internal Vmm Handle used by this instance.
        /// </summary>
        public Vmm Vmm { get; }

        /// <summary>
        /// Initializes a Vmm handle.
        /// Be sure you have 'vmm.dll' and 'leechcore.dll' in the current working directory.
        /// </summary>
        /// <param name="ConfigErrorInfo">Struct to receive Error Info.</param>
        /// <param name="args">Initialization arguments.</param>
        public VmmFrostHandle(out lc.CONFIG_ERRORINFO ConfigErrorInfo, params string[] args)
        {
            if (!Environment.Is64BitProcess)
                throw new PlatformNotSupportedException("This wrapper is designed for 64-bit apps.");
            Vmm = new(out ConfigErrorInfo, args);
        }
        /// <summary>
        /// Initializes a Vmm handle.
        /// Be sure you have 'vmm.dll' and 'leechcore.dll' in the current working directory.
        /// </summary>
        /// <param name="args">Initialization arguments.</param>
        public VmmFrostHandle(params string[] args)
        {
            if (!Environment.Is64BitProcess)
                throw new PlatformNotSupportedException("This wrapper is designed for 64-bit apps.");
            Vmm = new(args);
        }

        #region Utilities
        /// <summary>
        /// Gets a Memory Map from the Target Machine.
        /// </summary>
        /// <returns>Memory Map String. Can be written to a text file.</returns>
        public string GetMemoryMap()
        {
            try
            {
                var map = Vmm.Map_GetPhysMem();
                if (map.Length == 0) throw new Exception("Map_GetPhysMem() returned no entries!");
                var sb = new StringBuilder();
                sb.AppendFormat("{0,4}", "#")
                    .Append(' ') // Spacer [1]
                    .AppendFormat("{0,16}", "Base")
                    .Append("   ") // Spacer [3]
                    .AppendFormat("{0,16}", "Top")
                    .AppendLine();
                sb.AppendLine("-----------------------------------------");
                for (int i = 0; i < map.Length; i++)
                {
                    sb.AppendFormat("{0,4}", $"{i.ToString("D4")}")
                        .Append(' ') // Spacer [1]
                        .AppendFormat("{0,16}", $"{map[i].pa.ToString("x")}")
                        .Append(" - ") // Spacer [3]
                        .AppendFormat("{0,16}", $"{(map[i].pa + map[i].cb - 1).ToString("x")}")
                        .AppendLine();
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                throw new VmmException("Unable to acquire Memory Map!", ex);
            }
        }
        /// <summary>
        /// Gets a Process ID (PID) of a specified process.
        /// </summary>
        /// <param name="processName">Process to obtain PID from.</param>
        /// <returns>Process ID (PID).</returns>
        public uint GetPid(string processName)
        {
            try
            {
                if (!Vmm.PidGetFromName(processName, out uint pid))
                    throw new Exception("Unable to obtain PID.");
                return pid;
            }
            catch (Exception ex)
            {
                throw new VmmException("ERROR getting PID!", ex);
            }
        }
        /// <summary>
        /// Gets a Virtual Address of a specified Module.
        /// </summary>
        /// <param name="pid">Process ID to acquire module base from.</param>
        /// <param name="module">Name of module to acquire base from.</param>
        /// <returns>Virtual address of module base.</returns>
        public ulong GetModuleBase(uint pid, string module)
        {
            try
            {
                ulong result = Vmm.ProcessGetModuleBase(pid, module);
                if (result == 0x0) throw new Exception("Unable to get Module Base!");
                return result;
            }
            catch (Exception ex)
            {
                throw new VmmException("ERROR getting Module Base!", ex);
            }
        }
        #endregion

        #region Scatter Read
        /// <summary>
        /// Performs multiple reads in one sequence, significantly faster than singular reads.
        /// MemProcFS *does* have a new Scatter Read API, but it was significantly slower in my testing.
        /// This API also supports 'chaining' multiple rounds of reads.
        /// </summary>
        /// <param name="pid">Process ID to read from.</param>
        /// <param name="useCache">Use internal caching.</param>
        /// <param name="entries">Read entries.</param>
        public void ReadScatter(uint pid, bool useCache, params ScatterReadEntry[] entries)
        {
            var pagesToRead = new HashSet<ulong>(); // Will contain each unique page only once to prevent reading the same page multiple times
            foreach (var entry in entries) // First loop through all entries - GET INFO
            {
                // Parse Addr
                ulong addr = 0x0;
                if (entry.Addr is not null) // Ensure address field is set
                {
                    if (entry.Addr is ScatterReadEntry addrObj) // Check if the address references another ScatterRead Result
                    {
                        if (addrObj.TryGetResult<ulong>(out var refAddr)) // Use the referenced ScatterReadEntry's 'result' as the address
                        {
                            addr = refAddr;
                        }
                    }
                    else if (entry.Addr is ulong addrUlong)
                    {
                        addr = addrUlong;
                    }
                }
                entry.Addr = addr;

                // Parse Size
                uint size = 0;
                if (entry.Type.IsValueType) size = (uint)Marshal.SizeOf(entry.Type);
                else if (entry.Size is not null) // Check if size field is set
                {
                    if (entry.Size is ScatterReadEntry sizeObj) // Check if the size references another ScatterRead Result
                    {
                        if (sizeObj.TryGetResult<int>(out var refSize))
                        {
                            size = (uint)refSize;
                        }
                    }
                    else if (entry.Size is int sizeInt) // Check if the size references another ScatterRead Result
                    {
                        size = (uint)sizeInt;
                    }
                }
                entry.Size = (int)size;
                size *= (uint)entry.SizeMult;

                // INTEGRITY CHECK - Make sure the read is valid
                if (addr == 0x0 || size == 0)
                {
                    entry.IsFailed = true;
                    continue;
                }
                // location of object
                ulong readAddress = addr + entry.Offset;
                // get the number of pages
                uint numPages = ADDRESS_AND_SIZE_TO_SPAN_PAGES(readAddress, size);
                ulong basePage = PAGE_ALIGN(readAddress);

                //loop all the pages we would need
                for (int p = 0; p < numPages; p++)
                {
                    ulong page = basePage + PAGE_SIZE * (uint)p;
                    pagesToRead.Add(page);
                }
            }
            uint flags = useCache ? 0 : Vmm.FLAG_NOCACHE;
            var scatters = Vmm.MemReadScatter(pid, flags, pagesToRead.ToArray()); // execute scatter read

            foreach (var entry in entries) // Second loop through all entries - PARSE RESULTS
            {
                if (entry.IsFailed) // Skip this entry, leaves result as null
                    continue;

                ulong readAddress = (ulong)entry.Addr + entry.Offset; // location of object
                uint pageOffset = BYTE_OFFSET(readAddress); // Get object offset from the page start address

                uint size = (uint)((int)entry.Size * entry.SizeMult);
                var buffer = new byte[size]; // Alloc result buffer on heap
                int bytesCopied = 0; // track number of bytes copied to ensure nothing is missed
                uint cb = Math.Min(size, (uint)PAGE_SIZE - pageOffset); // bytes to read this page

                uint numPages = ADDRESS_AND_SIZE_TO_SPAN_PAGES(readAddress, size); // number of pages to read from (in case result spans multiple pages)
                ulong basePage = PAGE_ALIGN(readAddress);

                for (int p = 0; p < numPages; p++)
                {
                    ulong page = basePage + PAGE_SIZE * (uint)p; // get current page addr
                    var scatter = scatters.FirstOrDefault(x => x.qwA == page); // retrieve page of mem needed
                    if (scatter.f) // read succeeded -> copy to buffer
                    {
                        scatter.pb
                            .AsSpan((int)pageOffset, (int)cb)
                            .CopyTo(buffer.AsSpan(bytesCopied, (int)cb)); // Copy bytes to buffer
                        bytesCopied += (int)cb;
                    }
                    else // read failed -> set failed flag
                    {
                        entry.IsFailed = true;
                        break;
                    }

                    cb = (uint)PAGE_SIZE; // set bytes to read next page
                    if (((pageOffset + size) & 0xfff) != 0)
                        cb = ((pageOffset + size) & 0xfff);

                    pageOffset = 0; // Next page (if any) should start at 0
                }
                try // Parse buffer and set result
                {
                    if (entry.IsFailed) throw new Exception("Scatter read failed!");
                    else if (bytesCopied != size) throw new Exception("Incomplete buffer copy!");
                    else if (entry.Type == typeof(IntPtr)) // IntPtr becomes ulong
                    {
                        var addr = MemoryMarshal.Read<ulong>(buffer);
                        if (addr == 0x0) throw new Exception("NULLPTR");
                        entry.Result = addr;
                    }
                    else if (entry.Type == typeof(byte[]))
                    {
                        entry.Result = buffer;
                    }
                    else if (entry.Type == typeof(int))
                    {
                        entry.Result = MemoryMarshal.Read<int>(buffer);
                    }
                    else if (entry.Type == typeof(uint))
                    {
                        entry.Result = MemoryMarshal.Read<uint>(buffer);
                    }
                    else if (entry.Type == typeof(float))
                    {
                        entry.Result = MemoryMarshal.Read<float>(buffer);
                    }
                    else if (entry.Type == typeof(double))
                    {
                        entry.Result = MemoryMarshal.Read<double>(buffer);
                    }
                    else if (entry.Type == typeof(long))
                    {
                        entry.Result = MemoryMarshal.Read<long>(buffer);
                    }
                    else if (entry.Type == typeof(ulong))
                    {
                        entry.Result = MemoryMarshal.Read<ulong>(buffer);
                    }
                    else if (entry.Type == typeof(System.Numerics.Vector2))
                    {
                        entry.Result = MemoryMarshal.Read<System.Numerics.Vector2>(buffer);
                    }
                    else if (entry.Type == typeof(System.Numerics.Vector3))
                    {
                        entry.Result = MemoryMarshal.Read<System.Numerics.Vector3>(buffer);
                    }
                    else if (entry.Type == typeof(bool))
                    {
                        entry.Result = MemoryMarshal.Read<bool>(buffer);
                    }
                    else if (entry.Type == typeof(string))
                    {
                        entry.Result = Encoding.Default.GetString(buffer).Split('\0')[0]; // Null terminated str
                    }
                    else
                    {
                        Debug.WriteLine($"[Scatter Read] Type '{entry.Type}' not defined.");
                    }
                }
                catch
                {
                }
            }
        }
        #endregion

        #region Read Methods
        /// <summary>
        /// Read memory into a byte buffer.
        /// </summary>
        /// <param name="pid">Process ID to read from.</param>
        /// <param name="addr">Virtual address to read from.</param>
        /// <param name="size">Number of bytes to read.</param>
        /// <param name="useCache">Use internal caching.</param>
        /// <returns>Byte Array.</returns>
        public byte[] ReadBuffer(uint pid, ulong addr, int size, bool useCache = true)
        {
            try
            {
                uint flags = useCache ? 0 : Vmm.FLAG_NOCACHE;
                var buf = Vmm.MemRead(pid, addr, (uint)size, flags);
                if (buf.Length != size) throw new Exception("Incomplete memory read!");
                return buf;
            }
            catch (Exception ex)
            {
                throw new VmmException($"ERROR reading buffer at 0x{addr.ToString("X")}", ex);
            }
        }

        /// <summary>
        /// Reads a chain of pointers and returns the address from the final pointer.
        /// </summary>
        /// <param name="pid">Process ID to read from.</param>
        /// <param name="ptr">Virtual address of first pointer to read.</param>
        /// <param name="offsets">Subsequent offsets from the initial pointer.</param>
        /// <param name="useCache">Use internal caching.</param>
        /// <returns>Virtual address from final pointer.</returns>
        public ulong ReadPtrChain(uint pid, ulong ptr, uint[] offsets, bool useCache = true)
        {
            ulong addr = ptr; // push ptr to first address value
            for (int i = 0; i < offsets.Length; i++)
            {
                try
                {
                    addr = ReadPtr(pid, addr + offsets[i], useCache);
                }
                catch (Exception ex)
                {
                    throw new VmmException($"ERROR reading pointer chain at index {i}, addr 0x{addr.ToString("X")} + 0x{offsets[i].ToString("X")}", ex);
                }
            }
            return addr;
        }
        /// <summary>
        /// Reads a pointer and returns the address.
        /// </summary>
        /// <param name="pid">Process ID to read from.</param>
        /// <param name="ptr">Virtual address of pointer to read.</param>
        /// <param name="useCache">Use internal caching.</param>
        /// <returns>Virtual address from pointer. Will throw exception on NULLPTR.</returns>
        public ulong ReadPtr(uint pid, ulong ptr, bool useCache = true)
        {
            try
            {
                var addr = ReadValue<ulong>(pid, ptr, useCache);
                if (addr == 0x0) throw new Exception("NULLPTR");
                return addr;
            }
            catch (Exception ex)
            {
                throw new VmmException($"ERROR reading pointer at 0x{ptr.ToString("X")}", ex);
            }
        }

        /// <summary>
        /// Performs a Memory Read on a Value Type.
        /// </summary>
        /// <typeparam name="T">Type to read. Read Size is derived from the Type.</typeparam>
        /// <param name="pid">Process ID to read from.</param>
        /// <param name="addr">Virtual address of value to read.</param>
        /// <param name="useCache">Use internal caching.</param>
        /// <returns>Value <typeparamref name="T"/></returns>
        public T ReadValue<T>(uint pid, ulong addr, bool useCache = true)
            where T : struct
        {
            try
            {
                int size = Marshal.SizeOf(typeof(T));
                uint flags = useCache ? 0 : Vmm.FLAG_NOCACHE;
                var buf = Vmm.MemRead(pid, addr, (uint)size, flags);
                return MemoryMarshal.Read<T>(buf);
            }
            catch (Exception ex)
            {
                throw new VmmException($"ERROR reading {typeof(T)} value at 0x{addr.ToString("X")}", ex);
            }
        }

        /// <summary>
        /// Reads a null terminated string.
        /// </summary>
        /// <param name="pid">Process ID to read from.</param>
        /// <param name="addr">Virtual address of string to read.</param>
        /// <param name="length">Maximum number of bytes to read.</param>
        /// <param name="useCache">Use internal caching.</param>
        /// <returns>Null-Terminated String.</returns>
        public string ReadString(uint pid, ulong addr, uint length, bool useCache = true) // read n bytes (string)
        {
            try
            {
                uint flags = useCache ? 0 : Vmm.FLAG_NOCACHE;
                var buf = Vmm.MemRead(pid, addr, length, flags);
                return Encoding.Default.GetString(buf).Split('\0')[0]; // Terminate on null char
            }
            catch (Exception ex)
            {
                throw new VmmException($"ERROR reading string at 0x{addr.ToString("X")}", ex);
            }
        }
        #endregion

        #region Scatter Write
        /// <summary>
        /// Perform a Scatter Write Operation.
        /// </summary>
        /// <param name="pid">Process ID to write to.</param>
        /// <param name="entries">Write entries.</param>
        public void WriteScatter(uint pid, params ScatterWriteEntry[] entries)
        {
            try
            {
                using var hScatter = Vmm.Scatter_Initialize(pid, Vmm.FLAG_NOCACHE);
                foreach (var entry in entries)
                {
                    if (!hScatter.PrepareWrite(entry.Va, entry.Value))
                        throw new Exception($"ERROR preparing Scatter Write for entry 0x{entry.Va.ToString("X")}");
                }
                if (!hScatter.Execute())
                    throw new Exception("Scatter Write Failed!");
            }
            catch (Exception ex)
            {
                throw new VmmException($"ERROR executing Scatter Write!", ex);
            }
        }
        #endregion

        #region Write Methods
        /// <summary>
        /// Performs a Memory Write of a Value Type.
        /// </summary>
        /// <typeparam name="T">Type to write. Write Size is derived from the Type.</typeparam>
        /// <param name="pid">Process ID to write to.</param>
        /// <param name="addr">Virtual address to write to.</param>
        /// <param name="value">Value to write.</param>
        public void WriteValue<T>(uint pid, ulong addr, T value)
            where T : struct
        {
            try
            {
                var data = new byte[Marshal.SizeOf(typeof(T))];
                MemoryMarshal.Write(data, ref value);
                if (!Vmm.MemWrite(pid, addr, data))
                    throw new Exception("Memory write failed!");
            }
            catch (Exception ex)
            {
                throw new VmmException($"ERROR writing {typeof(T)} value at 0x{addr.ToString("X")}", ex);
            }
        }
        /// <summary>
        /// Performs a Memory Write of a Byte Array.
        /// </summary>
        /// <param name="pid">Process ID to write to.</param>
        /// <param name="addr">Virtual address to write to.</param>
        /// <param name="buffer">Byte array to write.</param>
        public void WriteBuffer(uint pid, ulong addr, byte[] buffer)
        {
            try
            {
                if (!Vmm.MemWrite(pid, addr, buffer))
                    throw new Exception("Memory write failed!");
            }
            catch (Exception ex)
            {
                throw new VmmException($"ERROR writing buffer ({buffer.Length} bytes) at 0x{addr.ToString("X")}", ex);
            }
        }
        #endregion

        #region Macros
        /// Mem Align Macros Ported from Win32
        private const ulong PAGE_SIZE = 0x1000;
        private const int PAGE_SHIFT = 12;

        /// <summary>
        /// The PAGE_ALIGN macro takes a virtual address and returns a page-aligned
        /// virtual address for that page.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong PAGE_ALIGN(ulong va)
        {
            return (va & ~(PAGE_SIZE - 1));
        }
        /// <summary>
        /// The ADDRESS_AND_SIZE_TO_SPAN_PAGES macro takes a virtual address and size and returns the number of pages spanned by the size.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ADDRESS_AND_SIZE_TO_SPAN_PAGES(ulong va, uint size)
        {
            return (uint)((BYTE_OFFSET(va) + (size) + (PAGE_SIZE - 1)) >> PAGE_SHIFT);
        }

        /// <summary>
        /// The BYTE_OFFSET macro takes a virtual address and returns the byte offset
        /// of that address within the page.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint BYTE_OFFSET(ulong va)
        {
            return (uint)(va & (PAGE_SIZE - 1));
        }
        #endregion

        #region IDisposable
        public void Dispose() => Vmm.Dispose();
        #endregion
    }
}