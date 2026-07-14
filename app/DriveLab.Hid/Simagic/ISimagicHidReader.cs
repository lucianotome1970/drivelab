namespace DriveLab.Hid.Simagic;

/// <summary>Seam de leitura do HID Simagic — permite testar o transporte sem hardware.</summary>
public interface ISimagicHidReader
{
    bool IsPresent();
    bool TryOpen();
    void Close();
    event EventHandler<byte[]>? ReportReceived;
}
