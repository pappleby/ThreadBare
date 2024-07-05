using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Yarn;
using Yarn.Compiler;

namespace ThreadBare
{
    internal class Compiler : YarnSpinnerParserBaseListener
    {
        string HeaderName = "script.yarn.h";
        string ScriptNamespace = "ThreadBare";
        Dictionary<string, Node> Nodes = new Dictionary<string, Node>();
        HashSet<string> NodeNames = new HashSet<string>();
        private int labelCount = 0;
        List<string> labels = new List<string>();

        public Node? CurrentNode { get; set; }

        internal static YarnSpinnerParser.HashtagContext? GetLineIDTag(YarnSpinnerParser.HashtagContext[] hashtagContexts)
        {
            // if there are any hashtags
            if (hashtagContexts != null)
            {
                foreach (var hashtagContext in hashtagContexts)
                {
                    string tagText = hashtagContext.text.Text;
                    if (tagText.StartsWith("line:", StringComparison.InvariantCulture))
                    {
                        return hashtagContext;
                    }
                }
            }

            return null;
        }
        /// <summary>
        /// Generates a unique label name to use in the program.
        /// </summary>
        /// <param name="commentary">Any additional text to append to the
        /// end of the label.</param>
        /// <returns>The new label name.</returns>
        internal string RegisterLabel(string commentary = null)
        {
            var label = "L" + this.labelCount++ + commentary;
            this.labels.Add(label);
            return label;
        }
        public string Compile()
        {
            var sb = new StringBuilder();
            sb.AppendLine("#pragma GCC diagnostic ignored \"-Wpedantic\"");
            sb.AppendLine($"""#include "{HeaderName}" """);
            sb.AppendLine($"""#include "script.h" """);
            sb.AppendLine("#include <bn_fixed.h>");
            sb.AppendLine($"namespace {ScriptNamespace} {{");
            foreach ( var node in Nodes.Values )
            {
                sb.Append(node.Compile());
            }
            sb.AppendLine("}");
            return sb.ToString(); 
        }
        public string CompileHeader()
        {
            var sb = new StringBuilder();
            sb.AppendLine("""
                #ifndef SCRIPT_YARN_H
                #define SCRIPT_YARN_H
                #include "threadbare.h"

                namespace ThreadBare {
                """);
            foreach(var nodeName in NodeNames)
            {
                sb.AppendLine($"\tvoid {nodeName}(TBScriptRunner& runner);");
            }
            sb.AppendLine();
            sb.Append("\tenum NodeLabel { nodestart = 0");
            this.labels.ForEach(label => sb.Append($", {label}"));
            sb.Append("};\n");
            sb.Append("""
                }
                #endif
                """);
            return sb.ToString();
        }
        public void SaveNodeNames()
        {
            NodeNames.UnionWith(Nodes.Keys);
        }
        public void ClearNodes() 
        {
                SaveNodeNames();
                Nodes.Clear();
        }
        /// <summary>
        /// we have found a new node set up the currentNode var ready to
        /// hold it and otherwise continue
        /// </summary>
        /// <inheritdoc/>
        public override void EnterNode(YarnSpinnerParser.NodeContext context)
        {
            this.CurrentNode = new Node(this);
        }
        /// <summary>
        /// have left the current node store it into the program wipe the
        /// var and make it ready to go again
        /// </summary>
        /// <inheritdoc />
        public override void ExitNode(YarnSpinnerParser.NodeContext context)
        {
            this.Nodes.Add(this.CurrentNode.Name, this.CurrentNode);
            this.CurrentNode = null;
        }


        /// <summary> 
        /// have finished with the header so about to enter the node body
        /// and all its statements do the initial setup required before
        /// compiling that body statements eg emit a new startlabel
        /// </summary>
        /// <inheritdoc />
        public override void ExitHeader(YarnSpinnerParser.HeaderContext context)
        {
            var headerKey = context.header_key.Text;

            // Use the header value if provided, else fall back to the
            // empty string. This means that a header like "foo: \n" will
            // be stored as 'foo', '', consistent with how it was typed.
            // That is, it's not null, because a header was provided, but
            // it was written as an empty line.
            var headerValue = context.header_value?.Text ?? String.Empty;

            if (headerKey.Equals("title", StringComparison.InvariantCulture))
            {
                // Set the name of the node
                this.CurrentNode.Name = headerValue;
            }

            if (headerKey.Equals("tags", StringComparison.InvariantCulture))
            {
                // Split the list of tags by spaces, and use that
                var tags = headerValue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                // this.CurrentNode.Tags.Add(tags);

                //if (this.CurrentNode.Tags.Contains("rawText"))
                //{
                //    // This is a raw text node. Flag it as such for future
                //    // compilation.
                //    this.RawTextNode = true;
                //}
            }

            var header = new Header();
            header.Key = headerKey;
            header.Value = headerValue;
            // this.CurrentNode.Headers.Add(header);
        }

        /// <summary>
        /// have entered the body the header should have finished being
        /// parsed and currentNode ready all we do is set up a body visitor
        /// and tell it to run through all the statements it handles
        /// everything from that point onwards
        /// </summary>
        /// <inheritdoc />
        public override void EnterBody(YarnSpinnerParser.BodyContext context)
        {
            var gbaVisitor = new GbaVisitor(this, "false");
            foreach (var statement in context.statement())
            {
                gbaVisitor.Visit(statement);
            }
            
        }


    }
    internal class Node
    {
        public Node(Compiler compiler)
        {
            this.compiler = compiler;
        }
        public Compiler compiler;
        public string Name = "node";
        List<Step> Steps = new List<Step>();
        public Stack<string> parameters = new Stack<string>();
        public void AddParameter(string p) { 
            parameters.Push(p);
        }
        public string FlushParamaters()
        {
            var debugString = string.Join(" ", parameters);
            parameters.Clear();
            return debugString;
        }
        public int stepCount => Steps.Count();
        public IExpressionDestination? GetCurrentLine()
        {
            var lastStep = this.Steps.LastOrDefault();
            if (lastStep is IExpressionDestination) { 
                return lastStep as IExpressionDestination; 
            }
            return null;
        } 
        public void AddStep(Step step)
        {
            Steps.Add(step);
        }
        public string Compile()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"\tvoid {Name}(TBScriptRunner& runner) {{");
            sb.AppendLine("\t\tswitch (runner.nextStep){case 0:");

            foreach (var step in Steps)
            {
                sb.Append(step.Compile(this));
            }
            // TODO: optimize away this end state if not needed
            sb.AppendLine("\t\trunner.Stop();");
            sb.AppendLine("\t\t}");
            sb.AppendLine("\t}");
            sb.AppendLine("");
            return sb.ToString();
        }
    }
    internal interface Step
    {
        public string Compile(Node node);
    }
    internal class Jump : Step
    {
        public string Target = "Start";
        public string Compile(Node node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"\t\trunner.Jump(&{Target});");
            sb.AppendLine("\t\treturn;");
            return sb.ToString();
        }
    }
    internal class Line : Step, IExpressionDestination
    {
        public string lineID = "line#";
        public string? condition;
        
        List<Expression> expressions = new List<Expression>();
        public void AddTextExpression(string textExpression)
        {
            var ce = expressions.LastOrDefault() as CalculatedExpression;
            if(ce != null && textExpression == "}") { return; }
            var te = expressions.LastOrDefault() as TextExpression;
            if(te == null)
            {
                this.expressions.Add(new TextExpression {text = textExpression });
            } else
            {
                te.text = te.text + textExpression;
            }
        }
        public void AddCalculatedExpression(string expression)
        {
            var te = expressions.LastOrDefault() as TextExpression;
            if (te != null && te.text.EndsWith('{')) { te.text = te.text.Remove(te.text.Length -1, 1); }
            this.expressions.Add(new CalculatedExpression { text = expression });
        }
        public string Compile(Node node)
        {
            var label = node.compiler.RegisterLabel();
            var sb = new StringBuilder();
            sb.AppendLine($"\t\t// {lineID}");
            sb.AppendLine("\t\trunner.currentLine.StartNewLine();");
            expressions.ForEach(expression =>
            {
                if (expression is TextExpression)
                {
                    var text = ((TextExpression)expression).text;
                    var trimmedExpression = text.ReplaceLineEndings("").Replace("\"", "\\\"");
                    sb.AppendLine($"\t\trunner.currentLine << \"{trimmedExpression}\";");
                } else if (expression is CalculatedExpression) {
                    var text = ((CalculatedExpression)expression).text;
                    sb.AppendLine($"\t\trunner.currentLine << {text};");
                }
                

            });
            sb.AppendLine($"\t\trunner.FinishLine({label});");
            sb.AppendLine("\t\treturn;");
            sb.AppendLine($"\t\tcase {label}:");
            sb.AppendLine();

            return sb.ToString();

        }

    }
    internal interface IExpressionDestination
    {
        public void AddCalculatedExpression(string expression);
        public void AddTextExpression(string textExpression);
    }
    internal class Expression {}
    internal class TextExpression : Expression { 
        public string text = "";
    }
    internal class CalculatedExpression : Expression 
    {
        public string text = "";
    }
    internal class Command : Step, IExpressionDestination
    {
        List<Expression> expressions = new List<Expression>();
        public void AddTextExpression(string textExpression)
        {
            //if (string.IsNullOrEmpty(commandName))
            //{
            //    var splits = textExpression.Trim().Split(" ");
            //    this.commandName = splits.First();
            //    textExpression = textExpression.Substring(commandName.Length).Trim();
            //}
            var ce = expressions.LastOrDefault() as CalculatedExpression;
            if (ce != null && textExpression == "}") { return; }
            var te = expressions.LastOrDefault() as TextExpression;
            if (te == null)
            {
                this.expressions.Add(new TextExpression { text = textExpression });
            }
            else
            {
                te.text = te.text + textExpression;
            }
        }
        public void AddCalculatedExpression(string expression)
        {
            var te = expressions.LastOrDefault() as TextExpression;
            if (te != null && te.text.EndsWith('{')) { te.text = te.text.Remove(te.text.Length - 1, 1); }
            this.expressions.Add(new CalculatedExpression { text = expression });
        }
        public string Compile(Node node)
        {
            List<string> args = new List<string>();

            foreach(var expression in expressions)
            {
                if (expression is TextExpression)
                {
                    var text = ((TextExpression)expression).text;
                    var splits = text.Split(" ");
                    foreach(var split in splits)
                    {
                        if (split.Trim().Length == 0) { continue; }
                        // Todo deal with args that should be string wrapped, variables, fixedpoint numbers, etc.
                        args.Add(split.Trim());
                    }
                    
                }
                else if (expression is CalculatedExpression)
                {
                    var text = ((CalculatedExpression)expression).text;
                    args.Add(text);
                }
            }
            var commandName = args.FirstOrDefault("error");
            args.RemoveAt(0);
            if(commandName == "stop")
            {
                var sb = new StringBuilder();
                sb.AppendLine("runner.Stop();");
                sb.AppendLine("return;");
                return sb.ToString();
            } else if (commandName == "wait")
            {
                var waitContinueLabel = node.compiler.RegisterLabel("waitContinue");
                var sb = new StringBuilder();
                sb.AppendLine($"\t\trunner.StartTimer({args[0]}, {waitContinueLabel});");
                sb.AppendLine("\t\treturn;");
                sb.AppendLine($"\t\tcase {waitContinueLabel}:\n");
                return sb.ToString();
            }
            var result = $"\t\t{commandName}({string.Join(", ", args)});\n\n";
            return result;
        }
    }
    
    internal class Set : Step
    {

        public string variable = "unknown";
        public string operation = "=";
        public string expression = "6";
        public string Compile(Node node)
        {
            var result = $"\t\trunner.variables.{variable} {operation} {expression};\n\n";
            return result;
        }
    }
    internal class If : Step
    {
        public string expression = "true";
        public string jumpIfFalseLabel = "";
        
        public string Compile(Node node)
        {
            var result = $"\t\tif(!({expression})){{\n\t\t\tgoto {jumpIfFalseLabel}; }}\n"; 
            return result;
        }
    }
    internal class GoTo: Step
    {
        public string targetLabel = "";
        public string Compile(Node node)
        {
            var result = $"\t\trunner.ReturnAndGoto({targetLabel}); return;\n";
            return result;
        }
    }
    internal class Label: Step
    {
        public string label = "";
        public string Compile(Node node)
        {
            var result = $"\t\tcase {label}:\n";
            return result;
        }
    }
    internal class Option : Step, IExpressionDestination
    {
        public int index = 0 ;
        public string? lineID = null;
        public string jumpToLabel = "";
        public string? condition;

        List<Expression> expressions = new List<Expression>();
        public void AddTextExpression(string textExpression)
        {
            var ce = expressions.LastOrDefault() as CalculatedExpression;
            if (ce != null && textExpression == "}") { return; }
            var te = expressions.LastOrDefault() as TextExpression;
            if (te == null)
            {
                this.expressions.Add(new TextExpression { text = textExpression });
            }
            else
            {
                te.text = te.text + textExpression;
            }
        }
        public void AddCalculatedExpression(string expression)
        {
            var te = expressions.LastOrDefault() as TextExpression;
            if (te != null && te.text.EndsWith('{')) { te.text = te.text.Remove(te.text.Length - 1, 1); }
            this.expressions.Add(new CalculatedExpression { text = expression });
        }
        public string Compile(Node node)
        {
            var lineId = node.compiler.RegisterLabel($"OptionLine{lineID}");

            var sb = new StringBuilder();
            sb.AppendLine($"\t\t\t// {lineId}");
            sb.AppendLine("\t\t{");
            sb.AppendLine($"\t\t\tauto currentOption = Option<OPTION_BUFFER_SIZE>({jumpToLabel});");
            if(condition != null)
            {
                sb.AppendLine($"\t\t\tcurrentOption.condition = {condition};");
            }
            expressions.ForEach(expression =>
            {
                if (expression is TextExpression)
                {
                    var text = ((TextExpression)expression).text;
                    var trimmedExpression = text.ReplaceLineEndings("").Replace("\"", "\\\"");
                    sb.AppendLine($"\t\t\tcurrentOption << \"{trimmedExpression}\";");
                }
                else if (expression is CalculatedExpression)
                {
                    var text = ((CalculatedExpression)expression).text;
                    sb.AppendLine($"\t\t\tcurrentOption << {text};");
                }
            sb.AppendLine($"\t\t\trunner.options.push_back(currentOption);");
            sb.AppendLine("\t\t}");

            });
            
            sb.AppendLine();

            return sb.ToString();

        }
    }
    internal class StartOptions: Step
    {
        public string Compile(Node node) {
            return "\t\t\n\t\trunner.options.clear();\n";
        }
    }
    internal class SendOptions : Step
    {
        public string Compile(Node node)
        {
            return "\t\trunner.state = Options;\n\t\treturn;\n\n";
        }
    }
}
