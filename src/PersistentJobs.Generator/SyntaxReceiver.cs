using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// https://github.com/dotnet/roslyn-sdk/blob/main/samples/CSharp/SourceGenerators/SourceGeneratorSamples/AutoNotifyGenerator.cs
namespace PersistentJobs.Generator
{
    class SyntaxReceiver : ISyntaxContextReceiver
    {
        public List<IMethodSymbol> MethodsWithCreateDeferredAttribute { get; } =
            new List<IMethodSymbol>();

        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            if (
                context.Node is MethodDeclarationSyntax methodDeclarationSyntax
                && methodDeclarationSyntax.AttributeLists.Count > 0
            )
            {
                var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclarationSyntax);

                if (
                    methodSymbol
                        .GetAttributes()
                        .Any(
                            x =>
                                x.AttributeClass.ToDisplayString()
                                == "PersistentJobs.CreateDeferredAttribute"
                        )
                )
                {
                    MethodsWithCreateDeferredAttribute.Add(methodSymbol);
                }
            }
        }
    }
}
