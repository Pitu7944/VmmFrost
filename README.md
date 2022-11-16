# VmmFrost
C#/.NET6 Wrapper for MemProcFS (Vmmsharp), with additional extensions.

Based on [Ulf Frisk](https://github.com/ufrisk)'s amazing [MemProcFS](https://github.com/ufrisk/MemProcFS) Library. Please support/sponsor him if you are able!

If you have a question with this API or encounter a bug, please open an [Issue](https://github.com/imerzan/VmmFrost/issues).

### Example on initializing a FPGA Connection:
```csharp
var mem = new VmmFrostHandle("-printf", "-v", "-device", "fpga");
```

### Scatter Read Example:
```csharp
var map = new ScatterReadMap(count, mem);
var round1 = map.AddRound(pid);
var round2 = map.AddRound(pid);
var round3 = map.AddRound(pid);
for (int i = 0; i < count; i++)
{
    var p1 = round1.AddEntry<IntPtr>(i, 0, someAddr + 0x10);
    var p2 = round2.AddEntry<IntPtr>(i, 1, p1, null, 0x50); // You can chain scatter read results between rounds
    var p3 = round3.AddEntry<IntPtr>(i, 2, p2, null, 0x100); // This allows you to read huge chains much more efficiently if you have to do hundreds or thousands of entries
}
map.Execute(); // execute scatter read
for (int i = 0; i < count; i++)
{
   if (!map.Results[i][2].TryGetResult<ulong>(out var p3)) // IntPtr is a substitute for ulong, but with nullptr check
    continue; // Read failed? Continue on
  Console.WriteLine($"p3 Result: {p3.ToString("X")}"); // Print result
}
```
