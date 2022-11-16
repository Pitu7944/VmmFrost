namespace VmmFrost.ScatterAPI
{
    /// <summary>
    /// Defines a scatter read round. Each round will execute a single scatter read. If you have reads that
    /// are dependent on previous reads (chained pointers for example), you may need multiple rounds.
    /// </summary>
    public sealed class ScatterReadRound
    {
        private readonly Dictionary<int, Dictionary<int, ScatterReadEntry>> _results;
        private readonly uint _pid;
        private readonly bool _useCache;
        private readonly List<ScatterReadEntry> _entries = new();
        public ScatterReadRound(Dictionary<int, Dictionary<int, ScatterReadEntry>> results, uint pid, bool useCache)
        {
            _results = results;
            _pid = pid;
            _useCache = useCache;
        }

        /// <summary>
        /// Adds a single Scatter Read Entry.
        /// </summary>
        /// <param name="index">For loop index this is associated with.</param>
        /// <param name="id">Random ID number to identify the entry's purpose.</param>
        /// <param name="addr">Address to read from (you can pass a ScatterReadEntry from an earlier round, 
        /// and it will use the result).</param>
        /// <param name="size">Size of oject to read (ONLY for reference types, value types get size from
        /// Type). You canc pass a ScatterReadEntry from an earlier round and it will use the Result.</param>
        /// <param name="offset">Optional offset to add to address (usually in the event that you pass a
        /// ScatterReadEntry to the Addr field).</param>
        /// <returns></returns>
        public ScatterReadEntry AddEntry<T>(int index, int id, object addr, object size = null, uint offset = 0x0)
        {
            var entry = new ScatterReadEntry()
            {
                Index = index,
                Id = id,
                Addr = addr,
                Type = typeof(T),
                Size = size,
                Offset = offset
            };
            _results[index].Add(id, entry);
            _entries.Add(entry);
            return entry;
        }

        /// <summary>
        /// Internal use only do not use.
        /// </summary>
        public void Run(VmmFrostHandle handle)
        {
            handle.ReadScatter(_pid, _useCache, _entries.ToArray());
        }
    }
}
