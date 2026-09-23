using DocuWareSageConnector.Infrastructure.Configuration;
using DocuWareSageConnector.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

EnvFileLoader.ApplyMissing(builder.Configuration, Path.Combine(builder.Environment.ContentRootPath, ".env"));
ConnectorStoreBootstrap.Load(builder.Configuration, builder.Environment.ContentRootPath);
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: true);
}

const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            return;
        }

        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddConnectorServices(builder.Configuration);

var app = builder.Build();

app.Logger.LogInformation(
    "Connector configuration is loaded from MySQL database {Database} on {Host}.",
    app.Configuration["ConnectorStore:Database"],
    app.Configuration["ConnectorStore:Host"]);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
