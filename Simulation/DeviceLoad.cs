namespace Battery.Simulation;

public sealed record DeviceLoad(string Name, double PowerDemandKW, int SocketNumber = 0);
