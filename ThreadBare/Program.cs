using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Yarn.Compiler;

namespace ThreadBare
{
    internal class Program
    {
        static void Main(string[] args)
        {
            ICharStream input = CharStreams.fromstring(""""
                title: Start
                ---
                ...Hmmm? You can't remember all that?
                Time for an options test
                -> Option A
                    You chose option A
                    -> Option C
                        You chose option C
                    -> Option D
                        You chose Option D
                -> Option B
                    You chose option B

                Oh, [wave]hello[/wave] there!
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

        }
    }
}
