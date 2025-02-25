using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using System.CommandLine;
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
            var includeHOption = new Option<String?>(
                    name: "-include",
                    description: "Header to include in generated cpp files)",
                    getDefaultValue: () => null
            );

            compileCommand.AddOption(inputOption.ExistingOnly());
            compileCommand.AddOption(outputHOption);
            compileCommand.AddOption(outputCppOption.ExistingOnly());
            compileCommand.AddOption(includeHOption);

            compileCommand.SetHandler(CompileFiles, inputOption, outputHOption, outputCppOption, includeHOption);

            compileCommand.Invoke(args);
        }

        static void CompileFiles(DirectoryInfo ysDir, DirectoryInfo hDir, DirectoryInfo cppDir, string? includeH)
        {

            var searchSubs = new EnumerationOptions { RecurseSubdirectories = true };
            var ysFiles = ysDir.EnumerateFiles("*.yarn", searchSubs);
            var oldHFiles = hDir.EnumerateFiles("*.yarn.h", searchSubs);
            var oldCppFiles = cppDir.EnumerateFiles("*.yarn.cpp", searchSubs);
            var compiler = new Compiler() { IncludeHeaderName = includeH };
            var definitionsListener = new DefinitionsListener { compiler = compiler };
            var gbaListener = new GbaListener { compiler = compiler };
            var filenameToTree = new Dictionary<string, YarnSpinnerParser.DialogueContext>();

            foreach (var ysFile in ysFiles)
            {
                if (ysFile == null || !ysFile.Exists)
                {
                    continue;
                }
                var input = CharStreams.fromPath(ysFile.FullName);
                YarnSpinnerLexer lexer = new YarnSpinnerLexer(input);
                CommonTokenStream tokens = new CommonTokenStream(lexer);
                YarnSpinnerParser parser = new YarnSpinnerParser(tokens);

                var tree = parser.dialogue();

                filenameToTree.Add(ysFile.Name, tree);
                compiler.CurrentFileName = ysFile.Name;
                ParseTreeWalker walker = new ParseTreeWalker();
                walker.Walk(definitionsListener, tree);

            }
            if (compiler.UnresolvedSmartVariables.Any())
            {
                compiler.ResolveSmartVariables();
            }
            foreach (var ysFileName_Tree in filenameToTree)
            {
                var ysFileName = ysFileName_Tree.Key;
                var tree = ysFileName_Tree.Value;

                ParseTreeWalker walker = new ParseTreeWalker();
                walker.Walk(gbaListener, tree);
                var compiledCpp = compiler.Compile(ysFileName);
                File.WriteAllText(Path.Combine(cppDir.FullName, ysFileName + ".cpp"), compiledCpp);
            }

            var utilityCpp = compiler.CompileUtilityCpp();
            File.WriteAllText(Path.Combine(cppDir.FullName, "_utility.cpp"), utilityCpp);

            // Attempt to compile node groups in their respective files, but need to write them in a new file if the source nodes are defined in different files
            var compiledHeader = compiler.CompileScriptHeader();
            File.WriteAllText(Path.Combine(hDir.FullName, "script.yarn.h"), compiledHeader);

        }

    }
}
