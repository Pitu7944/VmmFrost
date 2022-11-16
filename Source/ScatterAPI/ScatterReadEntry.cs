namespace VmmFrost.ScatterAPI
{
    /// <summary>
    /// Single scatter read entry. Use ScatterReadRound.AddEntry() to construct this class.
    /// </summary>
    public sealed class ScatterReadEntry
    {
        /// <summary>
        /// for loop index this is associated with
        /// </summary>
        public int Index { get; init; }
        /// <summary>
        /// Idenitifer code for this entry (for looking up Result).
        /// </summary>
        public int Id { get; init; }
        /// <summary>
        /// Can be an ulong or another ScatterReadEntry
        /// </summary>
        public object Addr { get; set; } = (ulong)0x0;
        /// <summary>
        /// Offset amount to be added to Address.
        /// </summary>
        public uint Offset { get; init; } = 0x0;
        /// <summary>
        /// Defines the type. For value types is also used to determine the size.
        /// </summary>
        public Type Type { get; init; }
        /// <summary>
        /// Can be an int32 or another ScatterReadEntry
        /// </summary>
        public object Size { get; set; }
        /// <summary>
        /// Multiplies size by this value. Default: 1
        /// </summary>
        public int SizeMult { get; set; } = 1;
        /// <summary>
        /// Result is stored here, must cast to unbox.
        /// </summary>
        public object Result
        {
            [Obsolete("Use TryGetResult instead.")]
            get;
            set;
        } = null;
        /// <summary>
        /// True if the scatter read has failed. Result will also be null.
        /// </summary>
        public bool IsFailed { get; set; } = false;

        /// <summary>
        /// Obtain result using specified type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="result">Result value to populate.</param>
        /// <returns>True if there was a successful result, otherwise false.</returns>
        public bool TryGetResult<T>(out T result)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (Result is not null &&
                Result is T)
            {
                result = (T)Result;
                return true;
            }
            result = default;
            return false;
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }
}
