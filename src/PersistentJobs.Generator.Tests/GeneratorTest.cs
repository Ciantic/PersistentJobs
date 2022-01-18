using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

/**
 * https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md#unit-testing-of-generators
 **/

namespace PersistentJobs.Generator.Tests;

public class GeneratorTests
{
    [Fact]
    public void SimpleGeneratorTest()
    {
        Compilation inputCompilation = CreateCompilation(
            @"
            using PersistentJobs;

            namespace MyCode
            {
                public partial class Program
                {
                    public class Input {
                        public string Something { get; set; }
                    }

                    [CreateDeferred]
                    private static Task DoSomething(Input input, AppDbContext dbContext) {
                        return Task.CompletedTask;
                    }

                    [Foo]
                    private static Task NotThis() {
                        return Task.CompletedTask;
                    }

                    public static void Main(string[] args)
                    {
                    }
                }
            }
            ",
            // Unit tests compiled code can't access dependencies, so this is
            // re-defined here
            @"
            using System;
            namespace PersistentJobs
            {
                [AttributeUsage(AttributeTargets.Method)]
                sealed class CreateDeferredAttribute : Attribute
                {
                }

                [AttributeUsage(AttributeTargets.Method)]
                sealed class FooAtribute : Attribute
                {
                }
            }
            "
        );

        var generator = new MySourceGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            inputCompilation,
            out var outputCompilation,
            out var diagnostics
        );
        // driver = driver.RunGeneratorsAndUpdateCompilation(
        //     outputCompilation,
        //     out var outputCompilation2,
        //     out var diagnostics2
        // );

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        var text = runResult.GeneratedTrees.First().GetText();

        var attributeSymbol = outputCompilation.GetTypeByMetadataName(
            "PersistentJobs.CreateDeferredAttribute"
        );

        Assert.Equal(3, outputCompilation.SyntaxTrees.Count());
        Assert.True(diagnostics.IsEmpty);
        // GeneratorDriverRunResult runResult = driver.GetRunResult();
        GeneratorRunResult generatorResult = runResult.Results[0];
        Assert.True(generatorResult.Generator == generator);
        Assert.True(generatorResult.Diagnostics.IsEmpty);
        Assert.True(generatorResult.GeneratedSources.Length == 1);
        Assert.True(generatorResult.Exception is null);
    }

    private static Compilation CreateCompilation(string source, string source2) =>
        CSharpCompilation.Create(
            "compilation",
            new[] { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(source2) },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(Binder).GetTypeInfo().Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
        );
}
