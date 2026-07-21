// ============================================================================
//  DriveLab
//  AccSharedMemorySource.cs — Fonte de telemetria do Assetto Corsa / Competizione via shared memory (Windows).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.IO.MemoryMappedFiles;

namespace DriveLab.Core.Telemetry.Sources;

/// <summary>
/// Lê a shared memory do Assetto Corsa (mesmo formato "SPageFile" do ACC e do AC — provavelmente do AC EVO):
/// os mapeamentos <c>Local\acpmf_physics</c> / <c>acpmf_graphics</c> / <c>acpmf_static</c>. Só existe no Windows
/// com o jogo rodando; em outros SO o <see cref="MemoryMappedFile.OpenExisting(string)"/> lança e a fonte fica
/// <see cref="IsAvailable"/>=false — então dá para compilar/rodar o app no Mac sem quebrar.
/// <para>Campos lidos por offset documentado e estável (packetId/gear/rpms/speed/maxRpm/status). As bandeiras
/// vivem num offset profundo que varia entre AC e ACC — ficam para um passo seguinte, validado no rig real.</para>
/// </summary>
public sealed class AccSharedMemorySource : IGameTelemetrySource
{
    // Offsets (bytes) — SPageFilePhysics
    private const int PhysicsGear = 16;   // int: 0=ré, 1=neutro, 2=1ª…
    private const int PhysicsRpms = 20;   // int
    private const int PhysicsSpeedKmh = 28;   // float
    // SPageFileStatic
    private const int StaticMaxRpm = 410;  // int
    // SPageFileGraphics
    private const int GraphicsStatus = 4;  // int: 0 OFF, 1 REPLAY, 2 LIVE, 3 PAUSE
    private const int StatusLive = 2;

    private MemoryMappedViewAccessor? _physics;
    private MemoryMappedViewAccessor? _static;
    private MemoryMappedViewAccessor? _graphics;

    public string Name => "ACC/AC";

    public bool IsAvailable => EnsureOpen();

    public bool TryRead(out GameTelemetry telemetry)
    {
        telemetry = default;
        if (!EnsureOpen() || _physics is null || _static is null || _graphics is null)
            return false;

        try
        {
            int status = _graphics.ReadInt32(GraphicsStatus);
            int rawGear = _physics.ReadInt32(PhysicsGear);
            int rpms = _physics.ReadInt32(PhysicsRpms);
            float speed = _physics.ReadSingle(PhysicsSpeedKmh);
            int maxRpm = _static.ReadInt32(StaticMaxRpm);

            telemetry = new GameTelemetry
            {
                Rpm = rpms,
                MaxRpm = maxRpm,
                ShiftRpm = 0,               // ACC não informa; o mapeador deriva de MaxRpm
                Gear = rawGear - 1,          // 0=ré→-1, 1=neutro→0, 2=1ª→1
                SpeedKmh = speed,
                Flag = GameFlag.None,        // TODO: mapear AC_FLAG_TYPE (offset validado no rig)
                PitLimiter = false,
                HasData = status == StatusLive && maxRpm > 0,
            };
            return true;
        }
        catch
        {
            Close();
            return false;
        }
    }

    private bool EnsureOpen()
    {
        if (_physics is not null && _static is not null && _graphics is not null)
            return true;
        try
        {
            _physics ??= Open("Local\\acpmf_physics");
            _static ??= Open("Local\\acpmf_static");
            _graphics ??= Open("Local\\acpmf_graphics");
            return _physics is not null && _static is not null && _graphics is not null;
        }
        catch
        {
            Close();
            return false;
        }
    }

    private static MemoryMappedViewAccessor? Open(string name)
    {
        try
        {
            var mmf = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            return mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        }
        catch
        {
            return null;   // jogo fechado / SO sem suporte → indisponível
        }
    }

    private void Close()
    {
        _physics?.Dispose(); _physics = null;
        _static?.Dispose(); _static = null;
        _graphics?.Dispose(); _graphics = null;
    }

    public void Dispose() => Close();
}
