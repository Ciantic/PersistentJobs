using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

// https://github.com/dotnet/roslyn-sdk/blob/main/samples/CSharp/SourceGenerators/SourceGeneratorSamples/AutoNotifyGenerator.cs
namespace PersistentJobs.Generator
{
    [Generator]
    public class DeferredGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            // Register a syntax receiver that will be created for each generation pass
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (!(context.SyntaxContextReceiver is SyntaxReceiver receiver))
                return;

            if (receiver.MethodsWithCreateDeferredAttribute.Count() == 0)
            {
                return;
            }

            var m = receiver.MethodsWithCreateDeferredAttribute.FirstOrDefault();
            var assemblyName = m.ContainingAssembly.ToDisplayString();
            var inputTypeName = m.Parameters[0].Type.ToDisplayString();
            var namespaceName = m.ContainingNamespace.ToDisplayString();
            var className = m.ContainingType.Name;
            var methodName = m.Name;
            var retType = m.ReturnType;
            var outputTypeName = "";
            if (retType is INamedTypeSymbol)
            {
                var taskType = retType as INamedTypeSymbol;
                if (taskType.Name != "Task")
                {
                    throw new Exception("Only tasks!");
                }

                if (!taskType.IsGenericType)
                {
                    throw new Exception("Only generic tasks");
                }

                var taskRetType = taskType.TypeArguments.First();

                // Get fully qualified outputname
                outputTypeName = taskRetType.ToDisplayString(
                    new SymbolDisplayFormat(
                        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                    )
                );
            }

            var source = FormattableString
                .Invariant(
                    $@"
                    using PersistentJobs;
                    using System.Threading.Tasks;

                    namespace {namespaceName}
                    {{ 
                        public partial class {className}
                        {{
                            
                            async public static Task<DeferredTask<{outputTypeName}>> {methodName}Deferred(
                                {inputTypeName} input, 
                                Microsoft.EntityFrameworkCore.DbContext context
                            ) 
                            {{
                                return await JobService.AddTask<{outputTypeName}>(context, {methodName}, input);
                            }}
                        }}
                    }}
                    "
                )
                .Replace("                    ", "");
            ;
            SourceText sourceText = SourceText.From(source, Encoding.UTF8);
            context.AddSource("Foo.g.cs", sourceText);
        }
    }
}
