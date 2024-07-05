using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using System;
using System.CommandLine;
using System.IO;
using Yarn.Compiler;

namespace ThreadBare
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var compileCommand = new RootCommand("Compiles a directory of ys into .cpp / .h files");
            var inputOption = new Option<DirectoryInfo>(
                    name: "-ys",
                    description: "Input directory for ys files (default: current directory)",
                    getDefaultValue: () => new DirectoryInfo(Environment.CurrentDirectory)
            );
            var outputCppOption = new Option<DirectoryInfo>(
                    name: "-cpp",
                    description: "Output directory for cpp files (default: current directory)",
                    getDefaultValue: () => new DirectoryInfo(Environment.CurrentDirectory)
            );
            var outputHOption = new Option<DirectoryInfo>(
                    name: "-h",
                    description: "Output directory for header files (default: current directory)",
                    getDefaultValue: () => new DirectoryInfo(Environment.CurrentDirectory)
            );

            compileCommand.AddOption(inputOption.ExistingOnly());
            compileCommand.AddOption(outputHOption);
            compileCommand.AddOption(outputCppOption.ExistingOnly());

            compileCommand.SetHandler(CompileFiles, inputOption, outputHOption, outputCppOption);

            compileCommand.Invoke(args);
        }

        static void CompileFiles(DirectoryInfo ysDir, DirectoryInfo hDir, DirectoryInfo cppDir) { 

            var searchSubs = new EnumerationOptions { RecurseSubdirectories = true };
            var ysFiles = ysDir.EnumerateFiles("*.yarn", searchSubs);
            var oldHFiles = hDir.EnumerateFiles("*.yarn.h", searchSubs);
            var oldCppFiles = cppDir.EnumerateFiles("*.yarn.cpp", searchSubs);
            var compiler = new Compiler();

            foreach (var ysFile in ysFiles)
            {
                if (ysFile == null || !ysFile.Exists) {
                    continue;
                }
                var compiledCpp = CompileFile(compiler, ysFile);
                compiler.ClearNodes();
                File.WriteAllText(Path.Combine(cppDir.FullName, ysFile.Name + ".cpp"), compiledCpp);
            }
            var compiledHeader = compiler.CompileHeader();
            File.WriteAllText(Path.Combine(hDir.FullName, "script.yarn.h"), compiledHeader);
        }

        static string CompileFile(Compiler compiler, FileInfo file)
        {
            var input = CharStreams.fromPath(file.FullName);
            YarnSpinnerLexer lexer = new YarnSpinnerLexer(input);
            CommonTokenStream tokens = new CommonTokenStream(lexer);
            YarnSpinnerParser parser = new YarnSpinnerParser(tokens);

            var tree = parser.dialogue();
            ParseTreeWalker walker = new ParseTreeWalker();
            walker.Walk(compiler, tree);
            var result = compiler.Compile();
            return result;
        }
    }
}
