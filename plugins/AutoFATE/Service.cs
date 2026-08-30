using AutoFATE.IPC;

namespace AutoFATE;

public static class Service {
    public static Configuration Config => Svc.Get<Configuration>();
    public static BossModIPC BossMod => Svc.Get<BossModIPC>();
    public static RotationSolverIPC RotationSolver => Svc.Get<RotationSolverIPC>();
    public static TextAdvanceIPC TextAdvance => Svc.Get<TextAdvanceIPC>();
}
