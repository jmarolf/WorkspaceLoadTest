using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.VisualBasic;
using NonBlocking;

namespace PerfTest
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkRunner
                .Run<WorkspaceBenchmark>(
                    ManualConfig
                        .Create(DefaultConfig.Instance)
                        .With(Job.Clr)
                        .With(ExecutionValidator.FailOnError));
        }
        
        public class WorkspaceBenchmark
        {

            public WorkspaceBenchmark()
            {
                var fileName = @"C:\Users\jmarolf\source\repos\WorkspaceLoadTest\WorkspaceLoadTest\commands.txt";
                Console.WriteLine($"Loading File '{fileName}'");
                var timer = Stopwatch.StartNew();
                var lines = File.ReadAllLines(fileName);
                Console.WriteLine($"> took {timer.ElapsedMilliseconds} ms");
                Console.WriteLine($"Parsing commandline args");
                timer = Stopwatch.StartNew();
                var arguments = lines.Select<string, CommandLineArguments>(x =>
                {
                    if (x.StartsWith("csc.exe"))
                    {
                        var arg = x.Replace("csc.exe", string.Empty);
                        return CSharpCommandLineParser.Default.Parse(arg.Split(' '), null, null);
                    }
                    else
                    {
                        var arg = x.Replace("vbc.exe", string.Empty);
                        return VisualBasicCommandLineParser.Default.Parse(arg.Split(' '), null, null);
                    }
                }).ToArray();
                Console.WriteLine($"> took {timer.ElapsedMilliseconds} ms");
                Arguments = arguments;
            }

            public static CommandLineArguments[] Arguments;

            [Benchmark]
            public void PopulateWorkspace()
            {
                var workspace = CreateWorkspaceFromCommandlineArgs(Arguments);
            }

            private static Workspace CreateWorkspaceFromCommandlineArgs(CommandLineArguments[] commandlines)
            {
                var workspace = new AdhocWorkspace();
                workspace.AddSolution(CreateSolutionInfo(commandlines));
                return workspace;
            }

            private static SolutionInfo CreateSolutionInfo(CommandLineArguments[] commandlines)
            {
                var projects = commandlines.AsParallel().Select(commandline =>
                {
                    if (commandline is CSharpCommandLineArguments)
                    {
                        return CreateProjectInfo(commandline, LanguageNames.CSharp);
                    }
                    if (commandline is VisualBasicCommandLineArguments)
                    {
                        return CreateProjectInfo(commandline, LanguageNames.VisualBasic);
                    }

                    return null;
                });
                return SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Default, projects: projects);
            }

            private static ProjectInfo CreateProjectInfo(CommandLineArguments args, string language)
            {
                ProjectId projectId = ProjectId.CreateNewId();
                return ProjectInfo.Create(
                    id: projectId,
                    version: VersionStamp.Default,
                    name: args.CompilationName ?? string.Empty,
                    assemblyName: args.OutputFileName ?? string.Empty,
                    language: language,
                    compilationOptions: args.CompilationOptions,
                    parseOptions: args.ParseOptions,
                    documents: GetDocumentInfos(projectId, args.SourceFiles),
                    projectReferences: new ProjectReference[] { },
                    metadataReferences: GetMetadataReferences(args.MetadataReferences),
                    analyzerReferences: GetAnalyzerReferences(args.AnalyzerReferences),
                    additionalDocuments: GetDocumentInfos(projectId, args.AdditionalFiles),
                    isSubmission: false,
                    hostObjectType: null);
            }

            private static IEnumerable<DocumentInfo> GetDocumentInfos(ProjectId projectId, ImmutableArray<CommandLineSourceFile> files)
                => files.Select(commandLineReference =>
                    DocumentInfo.Create(
                        id: DocumentId.CreateNewId(projectId),
                        name: Path.GetFileName(commandLineReference.Path),
                        filePath: commandLineReference.Path,
                        loader: new FileTextLoader(commandLineReference.Path, Encoding.UTF8)));

            private static IEnumerable<MetadataReference> GetMetadataReferences(ImmutableArray<CommandLineReference> metadataReferences)
                => metadataReferences.Select(commandLineReference => GetMetadateReference(commandLineReference));

            static ConcurrentDictionary<string, PortableExecutableReference> referenceCache = new ConcurrentDictionary<string, PortableExecutableReference>();

            private static PortableExecutableReference GetMetadateReference(CommandLineReference commandLineReference)
            {
                if (referenceCache.TryGetValue(commandLineReference.Reference, out var value))
                {
                    return value;
                }
                else
                {
                    if (File.Exists(commandLineReference.Reference))
                    {
                        var reference = MetadataReference.CreateFromFile(commandLineReference.Reference);
                        referenceCache.TryAdd(commandLineReference.Reference, reference);
                        return reference;
                    }

                    return null;
                }
            }

            private static IEnumerable<AnalyzerReference> GetAnalyzerReferences(ImmutableArray<CommandLineAnalyzerReference> commandLineAnalyzerReferences)
                => commandLineAnalyzerReferences.Select(commandLineAnalyzerReference => new AnalyzerFileReference(commandLineAnalyzerReference.FilePath, FromFileLoader.Instance));

            public class FromFileLoader : IAnalyzerAssemblyLoader
            {
                public static FromFileLoader Instance = new FromFileLoader();

                public void AddDependencyLocation(string fullPath) { }

                Assembly IAnalyzerAssemblyLoader.LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
            }
        }
    }
}
