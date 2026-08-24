using Vaxel.Datastar;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDatastarAdapter();

var app = builder.Build();

app.MapGet("/", () => "Vaxel conformance host — /test active");
app.MapDatastarTestEndpoint("/test");

app.Run();

public partial class Program;
