﻿using System.Runtime.InteropServices;

namespace VmmFrost.ScatterAPI
{
    /// <summary>
    /// Defines a Single Write in a Scatter Write Operation.
    /// </summary>
    public readonly struct ScatterWriteEntry
    {
        /// <summary>
        /// Virtual address to write to.
        /// </summary>
        public readonly ulong Va { get; init; }
        /// <summary>
        /// Value to write (in bytes).
        /// </summary>
        public readonly byte[] Value { get; init; }

        /// <summary>
        /// Creates a ScatterWriteEntry.
        /// </summary>
        /// <typeparam name="T">Value-Type</typeparam>
        /// <param name="va">Virtual address to write to.</param>
        /// <param name="value">Value to write.</param>
        /// <returns>ScatterWriteEntry</returns>
        public static ScatterWriteEntry Create<T>(ulong va, T value)
            where T : struct
        {
            var bytes = new byte[Marshal.SizeOf(typeof(T))];
            MemoryMarshal.Write(bytes, ref value);
            return new ScatterWriteEntry()
            {
                Va = va,
                Value = bytes
            };
        }
    }
}
