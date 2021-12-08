using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

// https://github.com/dotnet/roslyn-sdk/blob/main/samples/CSharp/SourceGenerators/SourceGeneratorSamples/AutoNotifyGenerator.cs
namespace PersistentJobs.Generator
{
    [Generator]
    public class MySourceGenerator : ISourceGenerator
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
            if (receiver.MethodsWithJobAttribute.Count() == 0)
            {
                return;
            }

            var m = receiver.MethodsWithJobAttribute.FirstOrDefault();
            var ns = m.ContainingNamespace.Name.ToString();

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

            var source = FormattableString.Invariant(
                $@"namespace {ns}
                {{ 
                    public class MyGeneratedClass
                    {{
                        public string HelloWorld() {{
                            return ""Hello World"";
                        }}

                        public string Goo() {{
                            return ""Foooo"";
                        }}
                    }}
                }}"
            );
            SourceText sourceText = SourceText.From(source, Encoding.UTF8);
            context.AddSource("HelloWorld.cs", sourceText);
        }
    }

    class SyntaxReceiver : ISyntaxContextReceiver
    {
        public List<IMethodSymbol> MethodsWithJobAttribute { get; } = new List<IMethodSymbol>();

        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            if (
                context.Node is MethodDeclarationSyntax methodDeclarationSyntax
                && methodDeclarationSyntax.AttributeLists.Count > 0
            )
            {
                var methodSymbol =
                    ModelExtensions.GetDeclaredSymbol(
                        context.SemanticModel,
                        methodDeclarationSyntax
                    ) as IMethodSymbol;

                if (
                    methodSymbol
                        .GetAttributes()
                        .Any(
                            x => x.AttributeClass.ToDisplayString() == "PersistentJobs.JobAttribute"
                        )
                )
                {
                    MethodsWithJobAttribute.Add(methodSymbol);
                }
            }
        }
    }
}
