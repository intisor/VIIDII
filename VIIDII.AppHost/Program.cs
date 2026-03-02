var builder = DistributedApplication.CreateBuilder(args);

var viidii = builder.AddProject<Projects.VIIDII>("viidii");

builder.Build().Run();
