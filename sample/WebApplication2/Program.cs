var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => typeof(Program).AssemblyQualifiedName);

app.Run();
