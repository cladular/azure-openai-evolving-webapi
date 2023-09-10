using System.Reflection;
using Azure.AI.OpenAI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

public interface IAICodeEngine
{
    object Execute(string operationType, string operation, string[] values);
    Task ImplementAsync(string operationType, string operation, int length, string[] examples);
    bool IsImplemented(string operation, string operation1);
}

internal class AICodeEngine : IAICodeEngine
{
    private readonly OpenAIClient _openAIClient;
    private readonly string _openAIDeployment;
    private readonly Dictionary<string, OperationInfo> _operations = new();
    private readonly IEnumerable<PortableExecutableReference> _coreReferences;

    public AICodeEngine(OpenAIClient openAIClient, string openAIDeployment)
    {
        _openAIClient = openAIClient;
        _openAIDeployment = openAIDeployment;

        _coreReferences =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    public bool IsImplemented(string operationType, string operation) => 
        _operations.ContainsKey($"{operationType}-{operation}");

    public async Task ImplementAsync(string operationType, string operation, int length, string[] examples)
    {
        var code = await GenerateCodeAsync(operationType, operation, length, examples);
        var type = CompileCode(code);
        var method = type.GetMethods().First();
        var instance = Activator.CreateInstance(type);

        _operations.Add($"{operationType}-{operation}", 
            new (instance, 
                method.GetParameters()
                    .Select(param => param.ParameterType)
                    .ToArray(), 
                method));
    }

    public object Execute(string operationType, string operation, string[] values)
    {
        var operationInfo = _operations[$"{operationType}-{operation}"];
        var args = values
          .Select((value, Index) => Convert.ChangeType(value, operationInfo.argsTypes[Index]))
          .ToArray();

        var result = operationInfo.MethodInfo.Invoke(operationInfo.OperationInstance, args);

        return result;
    }

    private async Task<string> GenerateCodeAsync(string operationType, string operation, int length, string[] examples)
    {
        ChatMessage initializationMessage = new(ChatRole.System,
            $"You are a code generation assitant that generates c# classes with random unique names for {operationType} operations. the genrated code should include common using directives. Namespace should have a random unique name. The generated result should be without explanation and without formatting");
     
        ChatMessage generateCodeMessage = new(ChatRole.User, 
            $"Generate a non-static {operation} function with a random unique name that accepts {length} arguments like {string.Join(" or ", examples)}");

        ChatCompletionsOptions chatCompletionsOptions = new(new [] { initializationMessage, generateCodeMessage })
        {
            Temperature = 0.5f
        };

        var response = await _openAIClient.GetChatCompletionsAsync(_openAIDeployment, chatCompletionsOptions);

        return response.Value.Choices[0].Message.Content;
    }

    private Type CompileCode(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);

        var compilation = CSharpCompilation
            .Create(Path.GetRandomFileName())
            .WithOptions(new (OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(_coreReferences)
            .AddSyntaxTrees(syntaxTree);

        var assembly = ReadAssembly(compilation);
        var type = assembly.GetTypes()
            .Where(type => !type.IsAssignableTo(typeof(Attribute)))
            .Single();
        
        return type;
    }

    private Assembly ReadAssembly(CSharpCompilation compilation)
    {
        using MemoryStream stream = new();

        compilation.Emit(stream);

        var assembly = Assembly.Load(stream.ToArray());
        
        return assembly;
    }
}

internal record OperationInfo(object OperationInstance, Type[] argsTypes, MethodInfo MethodInfo);