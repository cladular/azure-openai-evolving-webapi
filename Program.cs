using System.Reflection;
using Azure;
using Azure.AI.OpenAI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IAICodeEngine, AICodeEngine>((services) => 
    new AICodeEngine(
        services.GetRequiredService<OpenAIClient>(), 
        Environment.GetEnvironmentVariable("OPENAI_DEPOYMENT")));
builder.Services.AddSingleton(new OpenAIClient(
    new Uri(Environment.GetEnvironmentVariable("OPENAI_URI")),
    new AzureKeyCredential(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))));
builder.Configuration.AddEnvironmentVariables();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapMethods("/math/{operation}/{*url}", new[] { "GET" }, async (HttpContext context, IAICodeEngine codeGenerator) =>
{
    var operationType = "math";
    var operation = context.Request.RouteValues["operation"]?.ToString() ?? string.Empty;
    var valuesString = context.Request.RouteValues["url"]?.ToString() ?? string.Empty;
    var values = valuesString.Split('/');
    var method = context.Request.Method;

    if (!codeGenerator.IsImplemented(operationType, operation))
    {
        await codeGenerator.ImplementAsync(operationType, operation, values.Length, values);
    }
    
    return codeGenerator.Execute(operationType, operation, values);
});

app.Run();