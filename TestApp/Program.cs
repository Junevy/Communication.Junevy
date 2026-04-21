using System; using System.Threading.Tasks; class Program { static async Task Main() { await Task.Yield(); Memory<byte> m = new byte[10]; M(m.Span[2..]); } static void M(Span<byte> s) { } }
