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
            //debugmain();
            //return;

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
            var compiledHeader = compiler.CompileScriptHeader();
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

        static void debugmain()
        {
            ICharStream input = CharStreams.fromstring(""""
                title: Start
                tags: #camera2 nodetagtest:conductor_cabin
                ---
                <<detour OtherNode>>
                [nomarkup]Here's a big ol' [ bunch of ] characters, filled [[]] with square [[] brackets![/nomarkup]
                Here's a backslash! \\
                Here's some square brackets, just for you: \[ \]
                A [wave trimwhitespace=false/] B 
                A [wave/] B
                Oh, [wave]hello[/wave] there!
                does short work? [test=590 param2=12 t=40 g=95 /]
                Oh, [blister a=12]hello[/blister] there!
                
                
                

                tagtest #test #test2:value #tag3:7
                ...Hmmm? You can't remember all that?
                <<command>>
                Time for an options test
                -> Option A #otest
                    You chose option A
                    -> Option C #otest2:otestvalue
                        You chose option C
                    -> Option D
                        You chose Option D
                -> Option B
                    You chose option B

                Me: hello!
                This is a function test {1 + foobar(12, 1 + 4)} did it work?


                We can use "$name" in lines: 
                My name's {$name}!
                Yarn Spinner is a language for writing conversations in games!
                You can use it to write branching dialogue, and use that in a game engine!

                TEST {(4 + 2) >= $test }

                For example, here's a choice between some options!

                -> Wow, some options!
                    You got it, pal!
                -> Can I put text inside options?
                    You sure can!
                    For example, here's some lines inside an option.
                    You can even put options inside OTHER options!
                    -> Like this!
                        Wow!
                    -> Or this!
                        Incredible!

                // Comments start with two slashes, and won't show up in your conversation.
                // They're good for leaving notes!

                You can also write 'commands', which represent things that happen in the game!

                In this editor, they'll appear as text:

                <<fade_up 1.0>>
                <<fade_up 1.0 {$foo + $bar} >>
                -> Nice!
                    Right??
                -> But it didn't actually fade!
                    That's because this page doesn't know about 'fading', or any other feature. 
                    When you're testing your script on this page, we'll just show you your commands as text.
                    In a real game, you can define custom commands that do useful work!

                You can also use variables, which store information!

                Let's set a variable called "$name".

                <<set $name to "Yarn">>

                Done! You can see it appear in the list of variables at the bottom of the screen.

                We can use "$name" in lines: My name's {$name}!

                -> What can I store in variables?
                    You can store text, numbers, and true/false values!
                -> Where do variables get stored?
                    In this page, we store them in memory. When you use Yarn Spinner in a game engine, like Unity, you can store them in memory, or write custom code that stores them on disk alongside the rest of your saved game!

                We can also use 'if' statements to change what happens!

                Let's set a variable called '$gold' to 5.

                <<set $gold to 5>>

                Now lets decrement it

                <<set $gold -= 2>>

                Next, let's run different lines depending on what's stored inside '$gold':

                <<if $gold > 5>>
                    The '$gold' variable is bigger than 5!
                <<else>>
                    The '$gold' variable is 5 or less!
                <<endif>>

                Finally, we can use the "jump" command to go to a different node! Let's do that now!

                <<jump OtherNode>>
                ===
                title: OtherNode
                ---
                Here we are in a different node! Nodes let you divide up your content into different blocks, which makes it easier to manage.

                We're all done! Try changing the text in the editor to the left, and clicking Test again!
                ===
                """");

            YarnSpinnerLexer lexer = new YarnSpinnerLexer(input);
            CommonTokenStream tokens = new CommonTokenStream(lexer);

            YarnSpinnerParser parser = new YarnSpinnerParser(tokens);

            var tree = parser.dialogue();

            var compiler = new Compiler();
            ParseTreeWalker walker = new ParseTreeWalker();
            walker.Walk(compiler, tree);


            var result = compiler.Compile();

            Console.WriteLine(result);
            Console.WriteLine($"Max markups in line: {compiler.MarkupsInLineCount}");
            Console.WriteLine($"Max markup params in line: {compiler.MarkupParamsInLineCount}");
        }

    }
}
