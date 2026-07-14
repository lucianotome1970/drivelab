namespace DriveLab.Core.Protocol;

/// <summary>USB identity do freio de mão DriveLab. VID/PID de teste do pid.codes
/// (volante = 0x0001, pedaleira = 0x0002). Firmware enumera como "DriveLab Handbrake".</summary>
public static class HandbrakeDeviceIdentity
{
    public const int VendorId = 0x1209;
    public const int ProductId = 0x0003;
    public const byte ProtocolVersion = 1;
}
