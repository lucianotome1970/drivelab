using System.Diagnostics;
using System.Text;
using HidSharp;
using HidSharp.Reports;

// Utilitário de engenharia reversa de pedais HID (P1000/P2000 etc.).
// Uso:
//   dotnet run -- list
//   dotnet run -- watch <VID_hex> <PID_hex> [segundos]
// Ex.: dotnet run -- watch 0x0483 0x5750 30

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

switch (mode)
{
    case "list":
        ListDevices();
        break;
    case "watch" when args.Length >= 3:
        Watch(ParseHex(args[1]), ParseHex(args[2]),
              args.Length >= 4 && int.TryParse(args[3], out var s) ? s : 0);
        break;
    default:
        Console.WriteLine("uso: dotnet run -- list");
        Console.WriteLine("     dotnet run -- watch <VID_hex> <PID_hex> [segundos]");
        break;
}

static int ParseHex(string s) =>
    Convert.ToInt32(s.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);

static string Try(Func<string?> f)
{
    try { return f() ?? "?"; } catch { return "?"; }
}

static void ListDevices()
{
    Console.WriteLine("VID    PID    usagePage/usage  maxIn  fabricante / produto");
    Console.WriteLine(new string('-', 78));
    foreach (var d in DeviceList.Local.GetHidDevices())
    {
        int maxIn = 0;
        try { maxIn = d.GetMaxInputReportLength(); } catch { }

        var (up, usage) = TopUsage(d);
        var marker = up == 0x01 && (usage == 0x04 || usage == 0x05) ? "  <-- joystick/gamepad" : "";
        Console.WriteLine(
            $"0x{d.VendorID:X4} 0x{d.ProductID:X4}  0x{up:X2}/0x{usage:X2}         {maxIn,4}   " +
            $"{Try(d.GetManufacturer)} / {Try(d.GetProductName)}{marker}");
    }
}

static (int page, int usage) TopUsage(HidDevice d)
{
    try
    {
        var item = d.GetReportDescriptor().DeviceItems.FirstOrDefault();
        var u = item?.Usages.GetAllValues().FirstOrDefault() ?? 0;
        return ((int)(u >> 16), (int)(u & 0xFFFF));
    }
    catch { return (0, 0); }
}

static void Watch(int vid, int pid, int seconds)
{
    var dev = DeviceList.Local.GetHidDevices(vid, pid).FirstOrDefault();
    if (dev is null)
    {
        Console.WriteLine($"dispositivo 0x{vid:X4}:0x{pid:X4} não encontrado. Rode 'list' com ele plugado.");
        return;
    }

    Console.WriteLine($"Dispositivo: {Try(dev.GetManufacturer)} / {Try(dev.GetProductName)}  (0x{vid:X4}:0x{pid:X4})");

    // Report descriptor cru + estrutura dos input reports (eixos, ranges).
    try
    {
        var raw = dev.GetRawReportDescriptor();
        Console.WriteLine($"Report descriptor cru ({raw.Length} bytes): {BitConverter.ToString(raw)}");
    }
    catch (Exception e) { Console.WriteLine("  (descriptor cru indisponível: " + e.Message + ")"); }

    try
    {
        var rd = dev.GetReportDescriptor();
        foreach (var report in rd.InputReports)
        {
            Console.WriteLine($"Input report id={report.ReportID} tamanho={report.Length} bytes");
            foreach (var di in report.DataItems)
            {
                var usages = string.Join(",", di.Usages.GetAllValues().Select(x => $"0x{x:X}"));
                Console.WriteLine($"   count={di.ElementCount} bits={di.ElementBits} " +
                                  $"logico=[{di.LogicalMinimum}..{di.LogicalMaximum}] usages={usages}");
            }
        }
    }
    catch (Exception e) { Console.WriteLine("  (parse do descriptor indisponível: " + e.Message + ")"); }

    if (!dev.TryOpen(out var stream))
    {
        Console.WriteLine("NÃO consegui abrir o stream. No macOS talvez precise conceder 'Monitoramento de entrada' ");
        Console.WriteLine("ao Terminal em Ajustes do Sistema > Privacidade e Segurança > Monitoramento de entrada, e repetir.");
        return;
    }

    using (stream)
    {
        stream.ReadTimeout = 1000;
        int len = Math.Max(1, dev.GetMaxInputReportLength());
        var buf = new byte[len];
        var min = new int[len];
        var max = new int[len];
        Array.Fill(min, 255);

        byte[]? last = null;
        var sw = Stopwatch.StartNew();
        long lastSummary = 0;
        long deadline = seconds > 0 ? seconds * 1000L : long.MaxValue;

        Console.WriteLine(seconds > 0
            ? $"Capturando por {seconds}s — pise em CADA pedal até o fundo, um de cada vez..."
            : "Capturando (Ctrl+C para parar) — pise em cada pedal...");

        while (sw.ElapsedMilliseconds < deadline)
        {
            int n;
            try { n = stream.Read(buf, 0, buf.Length); }
            catch (TimeoutException) { continue; }
            if (n <= 0) continue;

            for (int i = 0; i < n; i++)
            {
                if (buf[i] < min[i]) min[i] = buf[i];
                if (buf[i] > max[i]) max[i] = buf[i];
            }

            if (last is null || !buf.AsSpan(0, n).SequenceEqual(last.AsSpan(0, n)))
            {
                Console.WriteLine($"[{sw.ElapsedMilliseconds,7}ms] {BitConverter.ToString(buf, 0, n)}");
                last = buf.AsSpan(0, n).ToArray();
            }

            if (sw.ElapsedMilliseconds - lastSummary > 2500)
            {
                lastSummary = sw.ElapsedMilliseconds;
                PrintRanges(min, max, n);
            }
        }

        Console.WriteLine("=== RESUMO FINAL — offsets que variaram (candidatos a eixos) ===");
        PrintRanges(min, max, len);
    }
}

static void PrintRanges(int[] min, int[] max, int n)
{
    var sb = new StringBuilder("  variação por offset [i] min..max: ");
    for (int i = 0; i < n; i++)
        if (max[i] > min[i])
            sb.Append($"[{i}]{min[i]}..{max[i]} ");
    Console.WriteLine(sb.ToString());
}
