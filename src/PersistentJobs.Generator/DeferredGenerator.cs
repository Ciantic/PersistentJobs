using System;
using System.Linq;
using System.Reflection;
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

            receiver.MethodsWithCreateDeferredAttribute.ForEach(
                m =>
                {
                    Generate(context, m);
                }
            );
        }

        private static void Generate(GeneratorExecutionContext context, IMethodSymbol methodSymbol)
        {
            // var m = receiver.MethodsWithCreateDeferredAttribute.FirstOrDefault();
            var inputTypeName = "";
            if (methodSymbol.Parameters.Length >= 1)
            {
                inputTypeName = methodSymbol.Parameters[0].Type.ToDisplayString();
            }

            var namespaceName = methodSymbol.ContainingNamespace.ToDisplayString();
            var className = methodSymbol.ContainingType.Name;
            var methodName = methodSymbol.Name;
            var retType = methodSymbol.ReturnType;
            var outputTypeName = "";
            if (retType is INamedTypeSymbol)
            {
                var taskType = retType as INamedTypeSymbol;
                if (taskType.Name != "Task")
                {
                    throw new Exception("Only tasks!");
                }

                if (taskType.IsGenericType)
                {
                    var taskRetType = taskType.TypeArguments.First();

                    // Get fully qualified outputname
                    outputTypeName = taskRetType.ToDisplayString(
                        new SymbolDisplayFormat(
                            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                        )
                    );
                }
            }

            var output = "Deferred";
            var addTaskGeneric = "";
            if (outputTypeName != "")
            {
                output = $"Deferred<{outputTypeName}>";
                addTaskGeneric = $"<{outputTypeName}>";
            }

            var inputArgOpt = "";
            var inputParam = "null";
            if (inputTypeName != "")
            {
                inputArgOpt = $"{inputTypeName} input, ";
                inputParam = "input";
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
                            
                            async public static Task<{output}> {methodName}Deferred(
                                {inputArgOpt} 
                                Microsoft.EntityFrameworkCore.DbContext context,
                                DeferredOptions? opts = null
                            ) 
                            {{
                                return await JobService.AddTask{addTaskGeneric}(context, {methodName}, {inputParam}, opts);
                            }}
                        }}
                    }}
                    "
                )
                .Replace("                    ", "");
            ;
            SourceText sourceText = SourceText.From(source, Encoding.UTF8);
            context.AddSource($"Deferred.{methodName}.g.cs", sourceText);
        }
    }
}
