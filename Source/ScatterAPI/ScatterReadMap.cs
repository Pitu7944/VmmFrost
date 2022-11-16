namespace VmmFrost.ScatterAPI
{
    /// <summary>
    /// Maps a Scatter Read Operation.
    /// </summary>
    public sealed class ScatterReadMap
    {
        private readonly List<ScatterReadRound> _rounds = new();
        private readonly Dictionary<int, Dictionary<int, ScatterReadEntry>> _results = new();
        private readonly VmmFrostHandle _handle;
        /// <summary>
        /// Contains results from Scatter Read after Execute() is performed. First key is Index, Second Key ID.
        /// </summary>
        public IReadOnlyDictionary<int, Dictionary<int, ScatterReadEntry>> Results { get => _results; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="indexCount">Number of indexes in the scatter read loop.</param>
        /// <param name="handle">VmmFrost handle to read with.</param>
        public ScatterReadMap(int indexCount, VmmFrostHandle handle)
        {
            _handle = handle;
            for (int i = 0; i < indexCount; i++)
            {
                _results.Add(i, new());
            }
        }

        /// <summary>
        /// Executes Scatter Read operation as defined per the map.
        /// </summary>
        public void Execute()
        {
            foreach (var round in _rounds)
            {
                round.Run(_handle);
            }
        }
        /// <summary>
        /// Add scatter read rounds to the operation. Each round is a successive scatter read, you may need multiple
        /// rounds if you have reads dependent on earlier scatter reads result(s).
        /// </summary>
        /// <returns>ScatterReadRound object.</returns>
        public ScatterReadRound AddRound(uint pid, bool useCache = true)
        {
            var round = new ScatterReadRound(_results, pid, useCache);
            _rounds.Add(round);
            return round;
        }
    }
}
