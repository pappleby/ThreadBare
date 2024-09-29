using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        public HashSet<string> NodeTags = new HashSet<string>();
        public HashSet<string> LineTags = new HashSet<string>();
        public HashSet<string> OptionTags = new HashSet<string>();
        public int LineOrOptionTagParamCount = 0;
        public HashSet<string> MarkupNames = new HashSet<string>();
        public int MarkupsInLineCount = 0;
        public int MarkupParamsInLineCount = 0;
        public int MaxOptionsCount = 0;

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

        public string Compile()
        {
            var sb = new StringBuilder();
            // sb.AppendLine("#pragma GCC diagnostic ignored \"-Wpedantic\"");
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
        public string CompileScriptHeader()
        {
            var sb = new StringBuilder();
            sb.AppendLine("""
                #ifndef SCRIPT_YARN_H
                #define SCRIPT_YARN_H

                namespace ThreadBare {
                """);
            sb.AppendLine("\tclass TBScriptRunner;");
            sb.AppendLine("\tclass NodeState;");
            sb.AppendLine($"\tconstexpr static int MAX_OPTIONS_COUNT = {MaxOptionsCount};");
            sb.AppendLine($"\tconstexpr static int MAX_TAGS_COUNT = {Math.Max(LineTags.Count(), OptionTags.Count())};");
            sb.AppendLine($"\tconstexpr static int MAX_TAG_PARAMS_COUNT = {LineOrOptionTagParamCount};");

            sb.AppendLine($"\tconstexpr static int MAX_ATTRIBUTES_COUNT = {MarkupsInLineCount};");
            sb.AppendLine($"\tconstexpr static int MAX_ATTRIBUTE_PARAMS_COUNT = {MarkupParamsInLineCount};");

            var joinedNodes = String.Join(", ", NodeNames);
            sb.AppendLine("\n\t// nodes names");
            sb.AppendLine($"\tenum class Node : int {{ {joinedNodes} }};");

            var joinedNodeTags = String.Join(", ", NodeTags);
            sb.AppendLine("\n\t// node tags:");
            sb.AppendLine($"\tenum class NodeTag : int {{ {joinedNodeTags} }};");

            var joinedTags = String.Join(", ", LineTags.Concat(OptionTags));
            sb.AppendLine("\n\t// tags:");
            sb.AppendLine($"\tenum class LineTag : int {{ {joinedTags} }};");

            var joinedAttributes = String.Join(", ", MarkupNames.SelectMany(m => new List<string> { m, "_" + m }));
            sb.AppendLine("\n\t// attributes:");
            sb.AppendLine($"\tenum class Attribute : int {{ {joinedAttributes} }};\n");

            sb.AppendLine($"\t// Nodes:");
            foreach (var nodeName in NodeNames)
            {
                sb.AppendLine($"\tvoid {nodeName}(TBScriptRunner& runner, NodeState& nodeState);");
            }

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
                foreach (var tag in tags)
                {
                    this.CurrentNode?.AddTag(tag);
                }

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
        List<Tag> tags = new List<Tag>();
        List<Step> Steps = new List<Step>();
        public Stack<string> parameters = new Stack<string>();
        private int labelCount = 1;
        List<string> labels = new List<string>();
        /// <summary>
        /// Generates a unique label name to use in the program.
        /// </summary>
        /// <param name="commentary">Any additional text to append to the
        /// end of the label.</param>
        /// <returns>The new label name.</returns>
        internal string RegisterLabel(string commentary = "")
        {
            var label = "L" + this.labelCount++ + commentary;
            this.labels.Add(label);
            return label;
        }
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
        public void AddTag(string text)
        {
            var tag = new Tag(TagLocation.Node, text);
            this.tags.Add(tag);
            this.compiler.NodeTags.Add(tag.Name);
        }

        public string Compile()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"\tvoid {Name}(TBScriptRunner& runner, NodeState& nodeState)\n\t{{");

            var sbSteps = new StringBuilder();
            sbSteps.AppendLine("\t\tswitch (nodeState.nextStep)");
            sbSteps.AppendLine("\t\t{");
            sbSteps.AppendLine("\t\tcase nodestart:");

            if (tags.Any())
            {
                sbSteps.AppendLine("\t\t\t// Node Tags:");
                var joinedTags = string.Join(", ", tags.Select(t => $"NodeTag::{t.Name}"));
                sbSteps.AppendLine($"\t\t\tfor(NodeTag p : {{{joinedTags}}}) {{ nodeState.tags.emplace_back(p);}}");
                if (tags.Any(t => t.Params.Any()))
                {
                    var joinedTagParams = String.Join(", ", tags.SelectMany(t => t.Params));
                    sbSteps.AppendLine($"\t\t\tfor(int p : {{{joinedTagParams}}}) {{ nodeState.tagParams.emplace_back(p);}}");
                }
            sbSteps.AppendLine();
            }

            foreach (var step in Steps)
            {
                sbSteps.Append(step.Compile(this));
            }
            if (Steps.OfType<Line>().Any()) { 
                sb.AppendLine("\t\tauto& currentLine = runner.currentLine;");
                if (Steps.OfType<Line>().Any(l => l.hasMarkup))
                {
                    sb.AppendLine("\t\tauto& markup = currentLine.markup;");
                }
            }

            sb.Append("\t\tenum NodeLabel { nodestart = 0");
            this.labels.ForEach(label => sb.Append($", {label}"));
            sb.Append("};\n");

            sb.Append(sbSteps.ToString());
            
            sb.AppendLine("\t\t\trunner.EndNode();");
            sb.AppendLine("\t\tdefault:");
            sb.AppendLine("\t\t\t//todo, throw error: invalid node step");
            sb.AppendLine("\t\t\tbreak;");
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
            sb.AppendLine($"\t\t\trunner.Jump(&{Target});");
            sb.AppendLine("\t\t\treturn;");
            return sb.ToString();
        }
    }
    internal class Detour : Step
    {
        public string Target = "Start";
        public string Compile(Node node)
        {
            var label = node.RegisterLabel($"DetourContinue_{Target}");
            var sb = new StringBuilder();
            sb.AppendLine($"\t\t\tnodeState.nextStep = {label};");
            sb.AppendLine($"\t\t\trunner.Detour(&{Target});");
            sb.AppendLine("\t\t\treturn;");
            sb.AppendLine($"\t\tcase {label}:");
            sb.AppendLine();

            return sb.ToString();
        }
    }
    internal class Line : Step, IExpressionDestination
    {
        public string lineID = "line#";
        public string? condition;
        
        List<Tag> tags = new List<Tag>();
        List<Expression> expressions = new List<Expression>();
        public bool hasMarkup = false;

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
        public void AddTag(Compiler compiler, string text)
        {
            this.hasMarkup = true;
            var tag = new Tag(TagLocation.Line, text);
            this.tags.Add(tag);
            compiler.LineTags.Add(tag.Name);
            compiler.LineOrOptionTagParamCount = Math.Max(compiler.LineOrOptionTagParamCount, tag.Params.Count());
        }
        public string Compile(Node node)
        {
            var label = node.RegisterLabel();
            var sb = new StringBuilder();
            sb.AppendLine($"\t\t\t// {lineID}");
            sb.AppendLine("\t\t\tcurrentLine.StartNewLine();");

            var expressionsWithMarkup = Markup.ExtractMarkup(expressions, node.compiler);

            expressionsWithMarkup.ForEach(expression =>
            {
                if (expression is TextExpression)
                {
                    var text = ((TextExpression)expression).text;
                    var trimmedExpression = text.ReplaceLineEndings("").Replace("\"", "\\\"");
                    sb.AppendLine($"\t\t\tcurrentLine << \"{trimmedExpression}\";");
                } else if (expression is CalculatedExpression) {
                    var text = ((CalculatedExpression)expression).text;
                    sb.AppendLine($"\t\t\tcurrentLine << {text};");
                } else if (expression is Markup)
                {
                    hasMarkup = true;
                    var markup = (Markup)expression;
                    sb.AppendLine($"\t\t\tmarkup.attributes.emplace_back(Attribute::{(markup.isStart?"":"_")}{markup.name});");
                    sb.AppendLine($"\t\t\tmarkup.attributePositions.emplace_back(currentLine.length());");

                    for (int i = 0; i < markup.parameters.Count; i++) 
                    {
                        var p = markup.parameters[i];
                        var pname = markup.parameterNames[i];
                        sb.AppendLine($"\t\t\tmarkup.attributeParams.emplace_back({p}); // {pname}");
                    }
                }
                

            });
            if (tags.Any())
            {
                var joinedTags = string.Join(", ", tags.Select(t=>$"LineTag::{t.Name}"));
                sb.AppendLine($"\t\t\tfor(LineTag p : {{{joinedTags}}}) {{ currentLine.markup.tags.emplace_back(p);}}");
                if (tags.Any(t => t.Params.Any()))
                {
                    var joinedTagParams = String.Join(", ", tags.SelectMany(t => t.Params));
                    sb.AppendLine($"\t\t\tfor(int p : {{{joinedTagParams}}}) {{ currentLine.markup.tagParams.emplace_back(p);}}");
                }
            }
            if (!string.IsNullOrWhiteSpace(condition))
            {
                sb.AppendLine($"\t\t\tcurrentLine.condition = {condition};");
            }
            sb.AppendLine($"\t\t\trunner.FinishLine({label});");
            sb.AppendLine("\t\t\treturn;");
            sb.AppendLine($"\t\tcase {label}:");
            sb.AppendLine();

            return sb.ToString();

        }

    }
    internal interface IExpressionDestination
    {
        public void AddCalculatedExpression(string expression);
        public void AddTextExpression(string textExpression);
        public void AddTag(Compiler compiler, string text);
    }
    internal class Expression {}
    internal class TextExpression : Expression { 
        public string text = "";
    }
    internal class CalculatedExpression : Expression 
    {
        public string text = "";
    }
    internal class Markup : Expression
    {
        public string name = "";
        public bool isStart = true;
        public List<string> parameters = new List<string>();
        // might be cleaner to have one list of key value pairs, then could export them in a consistant order
        public List<string> parameterNames = new List<string>();

        public Markup() { }
        protected static void EscapeAndAdd(List<Expression> result, string input)
        {
            var escaped = input.Replace("\\[", "[").Replace("\\]", "]").Replace("\\", "\\\\");
            if(!string.IsNullOrEmpty(escaped))
            {
                result.Add(new TextExpression { text = escaped });
            }
        }
        public static List<Expression> ExtractMarkup(List<Expression> expressions, Compiler compiler)
        {
            var result = new List<Expression>();
            var activeMarkups = new List<string>();

            Markup? currentMarkup = null;
            var isNoMarkupMode = false;
            foreach(var sourceExpression in expressions)
            {
                if(sourceExpression is CalculatedExpression) { 
                    var c = (CalculatedExpression)sourceExpression;
                    if(currentMarkup!=null) { currentMarkup.parameters.Add(c.text); }
                    else { result.Add(sourceExpression); }
                    continue;
                }
                
                var textExpression = sourceExpression as TextExpression;
                if (textExpression == null) { throw new Exception("Unexpected expression type in markup parser."); }
                var whitespaceTrimming = false;
                var currentText = textExpression.text;
                // iterate through text, gobbling up the next chunk as appropriate 
                while (currentText.Any())
                {
                    if(isNoMarkupMode)
                    {
                        var closePosition = currentText.IndexOf("[/nomarkup]");
                        if(closePosition == -1)
                        {
                            result.Add(new TextExpression { text = currentText });
                            break;
                        } else
                        {
                            isNoMarkupMode = false;
                            EscapeAndAdd(result, currentText.Substring(0, closePosition));
                            currentText = currentText.Substring(closePosition + "[/nomarkup]".Length);
                            continue;
                        }
                    }
                    if(currentMarkup != null)
                    {
                        currentText = currentText.TrimStart();
                        // Case 1: Handle end of markup
                        var isEndSelfClosing = currentText.StartsWith("/]");
                        var isEndNotSelfClosing = currentText.StartsWith("]");
                        if(isEndSelfClosing || isEndNotSelfClosing)
                        {
                            if(currentMarkup.name == "nomarkup")
                            {
                                isNoMarkupMode = true;
                                currentMarkup = null;
                                currentText = currentText.Substring(1);
                                continue;
                            }
                            compiler.MarkupNames.Add(currentMarkup.name);
                            result.Add(currentMarkup);
                            if (isEndNotSelfClosing)
                            {
                                activeMarkups.Add(currentMarkup.name);
                            }
                            currentMarkup = null;
                            currentText = currentText.Substring(isEndNotSelfClosing ? 1 : 2);
                            if(whitespaceTrimming && currentText.Any() && char.IsWhiteSpace(currentText[0]))
                            {
                                currentText = currentText.Substring(1);
                            }
                            continue;
                        }

                        // Case 2: Handle parameter name
                        var paramName = Regex.Match(currentText, @"([^]|\s|=])+");
                        currentMarkup.parameterNames.Add(paramName.Value);
                        currentText = currentText.Substring(paramName.Value.Length);
                        // Case 2.5: Handle parameter value, if any
                        if (currentText.StartsWith('='))
                        {
                            if(currentText.Length == 1) { 
                                // the value is a calculated expression, so it'll get adding in the next expression iteration
                                break; 
                            }
                            var paramValueBuilder = new StringBuilder();
                            currentText = currentText.Substring(1);
                            var isStringValue = false;
                            if (currentText.StartsWith('\"')) { isStringValue = true; currentText = currentText.Substring(1); paramValueBuilder.Append("\""); }
                            var isPreviousCharSlash = false;
                            while(currentText.Any())
                            {
                                var c = currentText[0];
                                // Don't want to gobble the closing brace
                                if (c == ']' && !isPreviousCharSlash || currentText.StartsWith("/]"))
                                {
                                    break;
                                }

                                currentText = currentText.Substring(1);
                                if (c == '"' && isStringValue && !isPreviousCharSlash)
                                {
                                    paramValueBuilder.Append('"');
                                    break;
                                }
                                else if (c == ' ' && !isStringValue)
                                {
                                    break;
                                }
                                else if (c == '\\' && !isPreviousCharSlash)
                                {
                                    isPreviousCharSlash = true;
                                    continue;
                                }
                                else 
                                {
                                    paramValueBuilder.Append(c);
                                }   
                            }
                            var paramValue = paramValueBuilder.ToString();
                            if(currentMarkup.parameterNames.Last() == "trimwhitespace") {
                                currentMarkup.parameterNames.Remove("trimwhitespace");
                                whitespaceTrimming &= paramValue != "false"; 
                            } else
                            {
                                currentMarkup.parameters.Add(paramValue);
                            }
                            
                            continue;
                        }
                        continue;    
                    }
                    // No current markup, so search in string to find the next one, if any
                    var match = Regex.Match(currentText, @"(?<!\\)(?:\\\\)*(\[[/]?\w*)");
                    if(!match.Success) { 
                        EscapeAndAdd(result, currentText);
                        break; }
                    if(match.Index == 0 || char.IsWhiteSpace(currentText[match.Index - 1]))
                    {
                        whitespaceTrimming = true;
                    }
                    EscapeAndAdd(result, currentText.Substring(0, match.Index));
                    currentText = currentText.Substring(match.Index + 1);
                    // This is a closing block
                    if (currentText.StartsWith('/'))
                    {
                        var closingName = match.Value.Substring("[/".Length);
                        // Close All
                        if (closingName.Length==0)
                        {
                            foreach(var markupToClose in activeMarkups)
                            {
                                result.Add(new Markup { isStart=false, name=markupToClose });
                            }
                            activeMarkups.Clear();
                            currentText = currentText.Substring("/]".Length);
                            continue;
                        }
                        
                        result.Add(new Markup { isStart = false, name= closingName });
                        activeMarkups.Remove(closingName);
                        currentText = currentText.Substring(closingName.Length + 2);
                        continue;
                    }
                    // We're starting a brand new markup
                    var name = match.Value.Substring(1);
                    currentMarkup = new Markup { name = name };
                    
                    if (!currentText.StartsWith($"{name}="))
                    {
                        // Not a shorthand property, so safe to gobble up the name
                        currentText = currentText.Substring(name.Length);
                    }
                }
            }
            var markupCount = result.OfType<Markup>().Count();
            var markupParamsCount = result.OfType<Markup>().SelectMany(m=>m.parameters).Count();

            compiler.MarkupsInLineCount = Math.Max(markupCount, compiler.MarkupsInLineCount);
            compiler.MarkupParamsInLineCount = Math.Max(markupParamsCount, compiler.MarkupParamsInLineCount);

            return result;
        }
    }
    internal class Command : Step, IExpressionDestination
    {
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
                var waitContinueLabel = node.RegisterLabel("waitContinue");
                var sb = new StringBuilder();
                sb.AppendLine($"\t\t\trunner.StartTimer({args[0]}, {waitContinueLabel});");
                sb.AppendLine("\t\t\treturn;");
                sb.AppendLine($"\t\tcase {waitContinueLabel}:\n");
                return sb.ToString();
            }
            var result = $"\t\t\t{commandName}({string.Join(", ", args)});\n\n";
            return result;
        }
        public void AddTag(Compiler compiler, string text)
        {
            throw new InvalidDataException("Commands can't have tags");
        }
    }
    
    internal class Set : Step
    {

        public string variable = "unknown";
        public string operation = "=";
        public string expression = "6";
        public string Compile(Node node)
        {
            var result = $"\t\t\trunner.variables.{variable} {operation} {expression};\n\n";
            return result;
        }
    }
    internal class If : Step
    {
        public string expression = "true";
        public string jumpIfFalseLabel = "";
        
        public string Compile(Node node)
        {
            var result = $"\t\t\tif(!({expression})){{\n\t\t\t\trunner.ReturnAndGoto({jumpIfFalseLabel});\n\t\t\t\treturn; }}\n"; 
            return result;
        }
    }
    internal class GoTo: Step
    {
        public string targetLabel = "";
        public string Compile(Node node)
        {
            var result = $"\t\t\trunner.ReturnAndGoto({targetLabel}); return;\n";
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

        List<Tag> tags = new List<Tag>();
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
            node.compiler.MaxOptionsCount = Math.Max(node.compiler.MaxOptionsCount, (this.index + 1));
            var lineId = node.RegisterLabel($"OptionLine{lineID}");

            var sb = new StringBuilder();
            sb.AppendLine($"\t\t\t// {lineId}");
            sb.AppendLine("\t\t\t{");
            sb.AppendLine($"\t\t\t\tauto currentOption = Option<OPTION_BUFFER_SIZE>({jumpToLabel});");
            if(condition != null)
            {
                sb.AppendLine($"\t\t\t\tcurrentOption.condition = {condition};");
            }
            var expressionsWithMarkup = Markup.ExtractMarkup(expressions, node.compiler);
            if(expressionsWithMarkup.OfType<Markup>().Any() || tags.Any())
            {
                sb.AppendLine($"\t\t\t\tauto& optionMarkup = currentOption.markup;");
            }
            
            expressionsWithMarkup.ForEach(expression =>
            {
                if (expression is TextExpression)
                {
                    var text = ((TextExpression)expression).text;
                    var trimmedExpression = text.ReplaceLineEndings("").Replace("\"", "\\\"");
                    sb.AppendLine($"\t\t\t\tcurrentOption << \"{trimmedExpression}\";");
                }
                else if (expression is CalculatedExpression)
                {
                    var text = ((CalculatedExpression)expression).text;
                    sb.AppendLine($"\t\t\t\tcurrentOption << {text};");
                }
                else if (expression is Markup markup)
                {
                    sb.AppendLine($"\t\t\t\toptionMarkup.attributes.emplace_back(Attribute::{(markup.isStart ? "" : "_")}{markup.name});");
                    sb.AppendLine($"\t\t\t\toptionMarkup.attributePositions.emplace_back(currentLine.length());");

                    for (int i = 0; i < markup.parameters.Count; i++)
                    {
                        var p = markup.parameters[i];
                        var pname = markup.parameterNames[i];
                        sb.AppendLine($"\t\t\t\toptionMarkup.attributeParams.emplace_back({p}); // {pname}");
                    }
                }
            });
            if (tags.Any())
            {
                var joinedTags = string.Join(", ", tags.Select(t => $"LineTag::{t.Name}"));
                sb.AppendLine($"\t\t\t\tfor(LineTag p : {{{joinedTags}}}) {{ optionMarkup.tags.emplace_back(p);}}");
                if (tags.Any(t => t.Params.Any()))
                {
                    var joinedTagParams = String.Join(", ", tags.SelectMany(t => t.Params));
                    sb.AppendLine($"\t\t\t\tfor(int p : {{{joinedTagParams}}}) {{ optionMarkup.tagParams.emplace_back(p);}}");
                }
            }
            sb.AppendLine($"\t\t\t\trunner.options.push_back(currentOption);");
            sb.AppendLine("\t\t\t}\n");


            return sb.ToString();

        }
        public void AddTag(Compiler compiler, string text)
        {
            var tag = new Tag(TagLocation.Option, text);
            this.tags.Add(tag);
            compiler.OptionTags.Add(tag.Name);
            compiler.LineOrOptionTagParamCount = Math.Max(compiler.LineOrOptionTagParamCount, tag.Params.Count());
        }
    }
    internal class StartOptions: Step
    {
        public string Compile(Node node) {
            return "\n\t\t\trunner.options.clear();\n";
        }
    }
    internal class SendOptions : Step
    {
        public string Compile(Node node)
        {
            return "\t\t\trunner.state = Options;\n\t\t\treturn;\n\n";
        }
    }

    public enum TagLocation { Node, Line, Option, Command };
    internal class Tag
    {
        public TagLocation Location { get; }
        public string Name { get; }
        public IEnumerable<string> Params { get; }
        public Tag(TagLocation location, string text)
        {
            this.Location = location;


            RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture;
            Regex regex = new Regex(@"^(#?(?<tagname>([a-z]+[a-z,\d])))($|\s|:(?<tagparams>([a-z,\d,:,_])*))\s*$", options);

            var parsedTag = regex.Matches(text)[0];
            this.Name = parsedTag.Groups["tagname"].Value;
            this.Params = parsedTag.Groups["tagparams"]
                ?.Value
                ?.Split(',')
                ?.Where(p=> !string.IsNullOrWhiteSpace(p))
                ?.Select(p =>
                {
                    if (p.Contains(':'))
                    {
                        return $"(int){p}";
                    }
                    else if (!int.TryParse(p, out _))
                    {
                        return $"\"{p}\"";
                    }
                    return p;
                }) ?? Enumerable.Empty<string>();
        }
        

    }


    
}
