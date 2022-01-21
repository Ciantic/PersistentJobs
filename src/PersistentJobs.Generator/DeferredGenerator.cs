using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
            var outputTypeName = m.ReturnType.Name;

            // var compilation = context.Compilation;
            // var attributeSymbol = compilation.GetTypeByMetadataName("PersistentJobs.JobAttribute");

            // var allNodes = compilation.SyntaxTrees.SelectMany(s => s.GetRoot().DescendantNodes());
            // var allClasses = allNodes
            //     .Where(d => d.IsKind(SyntaxKind.ClassDeclaration))
            //     .OfType<ClassDeclarationSyntax>();

            // var attributes = allNodes
            //     .Where(d => d.IsKind(SyntaxKind.Attribute))
            //     .OfType<AttributeSyntax>()
            //     .Where(d => d.Name.ToString() == "Job");

            var source = FormattableString
                .Invariant(
                    $@"
                    using PersistentJobs;
                    using System.Threading.Tasks;

                    namespace {namespaceName}
                    {{ 
                        public partial class {className}
                        {{
                            
                            [IsDeferred]
                            async public static Task<DeferredTask<{outputTypeName}>> {methodName}Deferred(
                                {inputTypeName} input, 
                                Microsoft.EntityFrameworkCore.DbContext context
                            ) 
                            {{
                                return await PersistentJob.Insert<{outputTypeName}>(context, {methodName}, input);
                                /*
                                await PersistentJob.InsertJob(
                                    context, 
                                    ""{assemblyName}"",
                                    ""{className}"",
                                    ""{methodName}"",
                                    input
                                );
                                */
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
