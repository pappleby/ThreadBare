using System.Text;
using System.Text.RegularExpressions;
using Yarn.Compiler;
using static Yarn.Compiler.YarnSpinnerParser;

namespace ThreadBare
{
    internal class Compiler
    {
        public string? IncludeHeaderName = null;
        string ScriptNamespace = "ThreadBare";
        public Dictionary<string, Node> Nodes = new Dictionary<string, Node>();

        public HashSet<string> NodeNames = new HashSet<string>();
        public HashSet<string> NodeGroupNames = new HashSet<string>();
        public HashSet<string> NodeTags = new HashSet<string>();
        public HashSet<string> LineTags = new HashSet<string>();
        public HashSet<string> OptionTags = new HashSet<string>();
        public HashSet<string> OnceVariables = new HashSet<string>();
        public int LineOrOptionTagParamCount = 0;
        public int NodeTagParamCount = 0;
        public HashSet<string> MarkupNames = new HashSet<string>();
        public int MarkupsInLineCount = 0;
        public int MarkupParamsInLineCount = 0;
        public int MaxOptionsCount = 0;
        public HashSet<string> VisitedNodeNames = new HashSet<string>();
        public HashSet<string> VisitedCountNodeNames = new HashSet<string>();
        public Dictionary<string, string> Variables = new Dictionary<string, string>();
        public Dictionary<string, SmartVariable> UnresolvedSmartVariables = new Dictionary<string, SmartVariable>();
        public Dictionary<string, string> ResolvedSmartVariables = new Dictionary<string, string>();

        public List<Enum> Enums = new List<Enum>();
        public string CurrentFileName = "";

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
        internal string RegisterOnceVariable()
        {
            var newName = $"once{OnceVariables.Count}";
            OnceVariables.Add(newName);
            return newName;
        }

        public string Compile(string filename)
        {
            if (UnresolvedSmartVariables.Any())
            {
                ResolveSmartVariables();
            }
            var sb = new StringBuilder();
            // sb.AppendLine("#pragma GCC diagnostic ignored \"-Wpedantic\"");
            if (!string.IsNullOrWhiteSpace(IncludeHeaderName))
            {
                sb.AppendLine($"""#include "{IncludeHeaderName}" """);
            }
            sb.AppendLine($"""#include "threadbare.h" """);
            sb.AppendLine("#include <bn_math.h>");
            sb.AppendLine("#include <bn_fixed.h>");
            sb.AppendLine($"namespace {ScriptNamespace} {{");
            var nodeGroupsToCompile = new List<string>();
            foreach (var node in Nodes.Values.Where(n => n.filename == filename))
            {
                sb.Append(node.Compile());
                if (node.isInNodeGroup && this.NodeGroupNames.Contains(node.OriginalName))
                {
                    nodeGroupsToCompile.Add(node.OriginalName);
                    this.NodeGroupNames.Remove(node.OriginalName);
                }
            }
            foreach (var nodeGroupName in nodeGroupsToCompile)
            {
                var compiledNodeGroup = CompileNodeGroupWrapper(nodeGroupName, filename);
                sb.AppendLine(compiledNodeGroup);
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
                """);
            sb.AppendLine("#include \"plural_select.h\"");
            sb.AppendLine("namespace ThreadBare {");
            sb.AppendLine("\tclass TBScriptRunner;");
            sb.AppendLine("\tclass NodeState;");
            sb.AppendLine($"\tconstexpr static int MAX_OPTIONS_COUNT = {Math.Max(1, MaxOptionsCount)};");
            sb.AppendLine($"\tconstexpr static int MAX_TAGS_COUNT = {Math.Max(1, Math.Max(LineTags.Count(), OptionTags.Count()))};");
            sb.AppendLine($"\tconstexpr static int MAX_TAG_PARAMS_COUNT = {Math.Max(1, LineOrOptionTagParamCount)};");

            sb.AppendLine($"\tconstexpr static int MAX_NODE_TAGS_COUNT = {Math.Max(1, NodeTags.Count())};");
            sb.AppendLine($"\tconstexpr static int MAX_NODE_TAG_PARAMS_COUNT = {Math.Max(1, NodeTagParamCount)};");


            sb.AppendLine($"\tconstexpr static int MAX_ATTRIBUTES_COUNT = {Math.Max(1, MarkupsInLineCount)};");
            sb.AppendLine($"\tconstexpr static int MAX_ATTRIBUTE_PARAMS_COUNT = {Math.Max(1, MarkupParamsInLineCount)};");

            // Need to round up to the nearest 8
            var vnc = VisitedNodeNames.Count();
            vnc = Math.Max(8, ((8 - (vnc % 8)) % 8) + vnc);
            sb.AppendLine($"\tconstexpr static int VISITED_NODE_COUNT = {vnc};");
            // Need at least 1 for array storage
            sb.AppendLine($"\tconstexpr static int VISIT_COUNT_NODE_COUNT = {Math.Max(1, VisitedCountNodeNames.Count())};");

            var onceCount = OnceVariables.Count();
            onceCount = Math.Max(8, ((8 - (onceCount % 8)) % 8) + onceCount);
            sb.AppendLine($"\tconstexpr static int ONCE_VARIABLE_COUNT = {onceCount};");


            var joinedNodes = string.Join(", ", NodeNames);
            sb.AppendLine("\n\t// nodes names");
            sb.AppendLine($"\tenum class Node : int {{ {joinedNodes} }};");

            var joinedNodeTags = string.Join(", ", NodeTags);
            sb.AppendLine("\n\t// nodes:");
            sb.AppendLine($"\tenum class NodeTag : int {{ {joinedNodeTags} }};");

            var vnn = string.Join(", ", VisitedNodeNames);
            sb.AppendLine($"\tenum class VisitedNodeName : int {{ {vnn} }};");

            var vcnn = string.Join(", ", VisitedCountNodeNames);
            sb.AppendLine($"\tenum class VisitCountedNodeName : int {{ {vcnn} }};");

            var onceNames = string.Join(", ", OnceVariables);
            sb.AppendLine($"\tenum class OnceKey : int {{ {onceNames} }};");

            var joinedTags = string.Join(", ", LineTags.Concat(OptionTags));
            sb.AppendLine("\n\t// tags:");
            sb.AppendLine($"\tenum class LineTag : int {{ {joinedTags} }};");

            var joinedAttributes = string.Join(", ", MarkupNames.SelectMany(m => new List<string> { m, "_" + m }));
            sb.AppendLine("\n\t// attributes:");
            sb.AppendLine($"\tenum class Attribute : int {{ {joinedAttributes} }};");

            sb.AppendLine($"\n\t// Nodes:");
            foreach (var nodeName in NodeNames)
            {
                sb.AppendLine($"\tvoid {nodeName}(TBScriptRunner& runner, NodeState& nodeState);");
            }

            sb.AppendLine("\n\t// Enums");
            foreach (var e in Enums)
            {
                sb.AppendLine(e.Compile());
            }

            sb.Append("""
                }
                #endif
                """);
            return sb.ToString();
        }

        private string CompileNodeGroupWrapper(string nodeGroupName, string filename)
        {
            var nodes = Nodes.Values.Where(n => n.OriginalName == nodeGroupName).ToList();
            var decisionNode = new Node { Name = nodeGroupName, compiler = this, filename = filename, isVisitCounted = this.VisitedCountNodeNames.Contains(nodeGroupName), isVisited = this.VisitedNodeNames.Contains(nodeGroupName) };
            string endOfGroupLabel = decisionNode.RegisterLabel("group_end");

            var labels = new List<string>();
            var onceLabels = new Dictionary<int, string>();
            int optionCount = 0;
            decisionNode.AddStep(new StartOptions());
            foreach (var node in nodes)
            {
                var optionStep = new Option { index = optionCount, isLineGroupItem = true, complexityCount = node.complexity };
                decisionNode.AddStep(optionStep);

                // Generate the name of internal label that we'll jump to if
                // this option is selected. We'll emit the label itself later.
                string optionDestinationLabel = decisionNode.RegisterLabel($"shortcutoption_{nodeGroupName}_{optionCount + 1}");
                labels.Add(optionDestinationLabel);
                optionStep.jumpToLabel = optionDestinationLabel;
                optionStep.conditions = node.conditions.ToList();
                if (node.isOnce)
                {
                    optionStep.conditions.Add($"!runner.VisitedNode({node.Name})");
                }

                optionCount++;
            }

            decisionNode.AddStep(new SendLineGroup { NoValidOptionsJumpTo = endOfGroupLabel });
            optionCount = 0;

            foreach (var node in nodes)
            {
                decisionNode.AddStep(new Label { label = labels[optionCount] });
                decisionNode.AddStep(new Jump { Target = node.Name, SafeJump = true });

                optionCount++;
            }
            decisionNode.AddStep(new Label { label = endOfGroupLabel });
            return decisionNode.Compile();
        }
        public void ResolveSmartVariables()
        {
            var expressionVisitor = new ExpressionsVisitor(this, "false");
            while (UnresolvedSmartVariables.Any())
            {
                var newlyResolved = new List<string>();
                foreach (var smart in UnresolvedSmartVariables.Values)
                {
                    if (smart.Dependencies.All(d => this.Variables.ContainsKey(d) || this.ResolvedSmartVariables.ContainsKey(d)))
                    {
                        // All dependencies are resolvable! Let's do that.
                        expressionVisitor.Visit(smart.Expression);
                        var resolvedExpression = expressionVisitor.FlushParamaters();
                        this.ResolvedSmartVariables.Add(smart.Name, resolvedExpression);
                        newlyResolved.Add(smart.Name);
                    }
                }
                foreach (var v in newlyResolved)
                {
                    UnresolvedSmartVariables.Remove(v);
                }
                if (!newlyResolved.Any())
                {
                    throw new Exception("Loop detected in smart variables!!!");
                }
            }
        }
    }
    internal class Node
    {
        public required Compiler compiler;
        public string Name = "node";
        public string OriginalName = "node";
        public bool isInNodeGroup = false;
        public string filename = "";
        public int nodeIntervalStart = 0; // used for disambiguating nodes in the same group 
        List<Tag> tags = new List<Tag>();
        List<Step> Steps = new List<Step>();
        //public Stack<string> parameters = new Stack<string>();
        private int labelCount = 1;
        List<string> labels = new List<string>();
        public bool isVisited = false;
        public bool isVisitCounted = false;
        public int onceCount = 0;
        public List<string> conditions = new List<string>();
        public int complexity = 0;
        public bool isOnce = false;

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
        internal string RegisterOnceLabel(string commentary = "")
        {
            var label = this.Name + "_once_" + this.onceCount++ + commentary;
            this.compiler.OnceVariables.Add(label);
            return label;
        }

        public int stepCount => Steps.Count();
        public IExpressionDestination? GetCurrentLine()
        {
            var lastStep = this.Steps.LastOrDefault();
            if (lastStep is IExpressionDestination)
            {
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
            var totalTags = this.tags.Count;
            var totalTagParamCount = this.tags.Select(t=> t.Params.Count()).Sum();
            compiler.NodeTagParamCount = Math.Max(compiler.NodeTagParamCount, totalTagParamCount);
        }

        public string Compile()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"\tvoid {Name}(TBScriptRunner& runner, NodeState& nodeState)\n\t{{");

            var sbSteps = new StringBuilder();
            sbSteps.AppendLine("\t\tswitch (nodeState.nextStep)");
            sbSteps.AppendLine("\t\t{");
            sbSteps.AppendLine("\t\tcase nodestart:");
            if (this.compiler.VisitedNodeNames.Contains(this.Name))
            {
                sbSteps.AppendLine($"\t\t\trunner.SetVisitedState(VisitedNodeName::{this.Name});");
            }
            if (this.compiler.VisitedCountNodeNames.Contains(this.Name))
            {
                sbSteps.AppendLine($"\t\t\trunner.IncrementVisitCount(VisitCountedNodeName::{this.Name});");
            }
            if (tags.Any())
            {
                sbSteps.AppendLine("\t\t\t// Node Tags:");
                var joinedTags = string.Join(", ", tags.Select(t => $"NodeTag::{t.Name}"));
                sbSteps.AppendLine($"\t\t\tfor(NodeTag p : {{{joinedTags}}}) {{ nodeState.tags.emplace_back(p);}}");
                if (tags.Any(t => t.Params.Any()))
                {
                    var joinedTagParams = string.Join(", ", tags.SelectMany(t => t.Params));
                    sbSteps.AppendLine($"\t\t\tfor(int p : {{{joinedTagParams}}}) {{ nodeState.tagParams.emplace_back(p);}}");
                }
                sbSteps.AppendLine();
            }

            foreach (var step in Steps)
            {
                sbSteps.Append(step.Compile(this));
            }
            if (Steps.OfType<Line>().Any())
            {
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
            sb.AppendLine("\t\t\treturn;");
            sb.AppendLine("\t\tdefault:");
            sb.AppendLine($"\t\t\tBN_ASSERT(false, \"invalid node step: \", nodeState.nextStep, \" in node {Name}\");");
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
        public bool SafeJump = false;
        public string Compile(Node node)
        {
            var sb = new StringBuilder();
            if (SafeJump)
            {
                sb.AppendLine($"\t\t\trunner.SafeJump(&{Target});");
            }
            else
            {
                sb.AppendLine($"\t\t\trunner.Jump(&{Target});");
            }
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
        public bool once = false;

        List<Tag> tags = new List<Tag>();
        List<Expression> expressions = new List<Expression>();
        public bool hasMarkup = false;

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

            var expressionsWithMarkup = Markup.ExtractMarkup(expressions, isLine: true, node.compiler);

            expressionsWithMarkup.ForEach(expression =>
            {
                if (expression is TextExpression te)
                {
                    var text = te.text;
                    var trimmedExpression = text.ReplaceLineEndings("").Replace("\"", "\\\"");
                    sb.AppendLine($"\t\t\tcurrentLine << \"{trimmedExpression}\";");
                }
                else if (expression is CalculatedExpression ce)
                {
                    var text = ce.text;
                    sb.AppendLine($"\t\t\tcurrentLine << {text};");
                }
                else if (expression is Markup markup)
                {
                    hasMarkup = true;
                    sb.AppendLine($"\t\t\tmarkup.attributes.emplace_back(Attribute::{(markup.isStart ? "" : "_")}{markup.name});");
                    sb.AppendLine($"\t\t\tmarkup.attributePositions.emplace_back(currentLine.length());");

                    for (int i = 0; i < markup.parameters.Count; i++)
                    {
                        var p = markup.parameters[i];
                        var pname = markup.parameterNames[i];
                        sb.AppendLine($"\t\t\tmarkup.attributeParams.emplace_back({p}); // {pname}");
                    }
                }
                else if (expression is Select select)
                {
                    sb.AppendLine(select.getText());
                }
                else if (expression is PluralMarkup pm)
                {
                    sb.AppendLine(pm.getText());
                }
            });
            if (tags.Any())
            {
                var joinedTags = string.Join(", ", tags.Select(t => $"LineTag::{t.Name}"));
                sb.AppendLine($"\t\t\tfor(LineTag p : {{{joinedTags}}}) {{ currentLine.markup.tags.emplace_back(p);}}");
                if (tags.Any(t => t.Params.Any()))
                {
                    var joinedTagParams = string.Join(", ", tags.SelectMany(t => t.Params));
                    sb.AppendLine($"\t\t\tfor(int p : {{{joinedTagParams}}}) {{ currentLine.markup.tagParams.emplace_back(p);}}");
                }
            }
            var conditions = new List<string>();
            string onceLabel = "";
            if (once)
            {
                onceLabel = node.RegisterOnceLabel(lineID);
                conditions.Add($"!runner.Once(OnceKey::{onceLabel})");
            }
            if (!string.IsNullOrWhiteSpace(condition))
            {
                conditions.Add(condition);
            }
            if (conditions.Any())
            {
                var joinedConditions = string.Join(" && ", conditions);
                sb.AppendLine($"\t\t\tcurrentLine.condition = ({joinedConditions});");
            }
            if (once)
            {
                sb.AppendLine($"\t\t\trunner.Once(OnceKey::{onceLabel}) &= currentLine.condition;");
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
    internal class Expression { }
    internal class TextExpression : Expression
    {
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
            if (!string.IsNullOrEmpty(escaped))
            {
                result.Add(new TextExpression { text = escaped });
            }
        }
        protected static List<string> MarkupExcludeList = new List<string> { "select", "plural", "ordinal" };
        public static List<Expression> ExtractMarkup(List<Expression> expressions, bool isLine, Compiler compiler)
        {
            var result = new List<Expression>();
            var activeMarkups = new List<string>();

            Markup? currentMarkup = null;
            var isNoMarkupMode = false;
            foreach (var sourceExpression in expressions)
            {
                if (sourceExpression is CalculatedExpression)
                {
                    var c = (CalculatedExpression)sourceExpression;
                    if (currentMarkup != null) { currentMarkup.parameters.Add(c.text); }
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
                    if (isNoMarkupMode)
                    {
                        var closePosition = currentText.IndexOf("[/nomarkup]");
                        if (closePosition == -1)
                        {
                            result.Add(new TextExpression { text = currentText });
                            break;
                        }
                        else
                        {
                            isNoMarkupMode = false;
                            EscapeAndAdd(result, currentText.Substring(0, closePosition));
                            currentText = currentText.Substring(closePosition + "[/nomarkup]".Length);
                            continue;
                        }
                    }
                    if (currentMarkup != null)
                    {
                        currentText = currentText.TrimStart();
                        // Case 1: Handle end of markup
                        var isEndSelfClosing = currentText.StartsWith("/]");
                        var isEndNotSelfClosing = currentText.StartsWith("]");
                        if (isEndSelfClosing || isEndNotSelfClosing)
                        {
                            if (currentMarkup.name == "nomarkup")
                            {
                                isNoMarkupMode = true;
                                currentMarkup = null;
                                currentText = currentText.Substring(1);
                                continue;
                            }
                            if (!MarkupExcludeList.Contains(currentMarkup.name))
                            {
                                compiler.MarkupNames.Add(currentMarkup.name);
                            }
                            result.Add(currentMarkup);
                            if (isEndNotSelfClosing)
                            {
                                activeMarkups.Add(currentMarkup.name);
                            }
                            currentMarkup = null;
                            currentText = currentText.Substring(isEndNotSelfClosing ? 1 : 2);
                            if (whitespaceTrimming && currentText.Any() && char.IsWhiteSpace(currentText[0]))
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
                            if (currentText.Length == 1)
                            {
                                // the value is a calculated expression, so it'll get adding in the next expression iteration
                                break;
                            }
                            var paramValueBuilder = new StringBuilder();
                            currentText = currentText.Substring(1);
                            var isStringValue = false;
                            if (currentText.StartsWith('\"')) { isStringValue = true; currentText = currentText.Substring(1); paramValueBuilder.Append("\""); }
                            var isPreviousCharSlash = false;
                            while (currentText.Any())
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
                            if (currentMarkup.parameterNames.Last() == "trimwhitespace")
                            {
                                currentMarkup.parameterNames.Remove("trimwhitespace");
                                whitespaceTrimming &= paramValue != "false";
                            }
                            else
                            {
                                currentMarkup.parameters.Add(paramValue);
                            }

                            continue;
                        }
                        continue;
                    }
                    // No current markup, so search in string to find the next one, if any
                    var match = Regex.Match(currentText, @"(?<!\\)(?:\\\\)*(\[[/]?\w*)");
                    if (!match.Success)
                    {
                        EscapeAndAdd(result, currentText);
                        break;
                    }
                    if (match.Index == 0 || char.IsWhiteSpace(currentText[match.Index - 1]))
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
                        if (closingName.Length == 0)
                        {
                            foreach (var markupToClose in activeMarkups)
                            {
                                result.Add(new Markup { isStart = false, name = markupToClose });
                            }
                            activeMarkups.Clear();
                            currentText = currentText.Substring("/]".Length);
                            continue;
                        }

                        result.Add(new Markup { isStart = false, name = closingName });
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

            for (int i = 0; i < result.Count; i++)
            {
                var resultItem = result[i];
                if (resultItem is Markup markup && markup.name == "select")
                {
                    result.RemoveAt(i);
                    var selectExpression = new Select(markup, isLine, compiler);
                    result.Insert(i, selectExpression);
                }
                else if (resultItem is Markup pm && (pm.name == "plural" || pm.name == "ordinal"))
                {
                    result.RemoveAt(i);
                    var pluralExpression = new PluralMarkup(pm, isLine, compiler);
                    result.Insert(i, pluralExpression);
                }
            }


            var markupCount = result.OfType<Markup>().Count();
            var markupParamsCount = result.OfType<Markup>().SelectMany(m => m.parameters).Count();




            var needCharacter = isLine && !result.OfType<Markup>().Any(m => m.name == "character");
            if (needCharacter)
            {
                // Potentially strip out a leading ":" if line dummy's out the character
                var firstResult = result.First();
                if (firstResult is TextExpression te)
                {
                    if (te.text.StartsWith(':'))
                    {
                        te.text = te.text.Substring(1);
                        needCharacter = false;
                    }
                }
            }

            if (needCharacter)
            {
                for (var i = 0; i < result.Count(); i++)
                {
                    var te = result[i] as TextExpression;
                    if (te == null)
                    {
                        continue;
                    }
                    var text = te.text.ToString();
                    if (text.Contains(":"))
                    {
                        markupCount += 2;
                        compiler.MarkupNames.Add("character");
                        result.RemoveAt(i); // remove the unsplit text element
                        var textParts = text.Split(":", 2, StringSplitOptions.TrimEntries);
                        // inserting in reverse order
                        if (!string.IsNullOrWhiteSpace(textParts[1]))
                        {
                            result.Insert(i, new TextExpression { text = textParts[1] });
                        }
                        result.Insert(i, new Markup { name = "character", isStart = false });
                        if (!string.IsNullOrWhiteSpace(textParts[0]))
                        {
                            result.Insert(i, new TextExpression { text = textParts[0] });
                        }
                        result.Insert(0, new Markup { name = "character" });
                        break;
                    }

                }
            }

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
        public void AddCalculatedExpression(string? expression)
        {
            if (expression == null)
            {
                //todo: maybe should throw an error here?
                return;
            }
            var te = expressions.LastOrDefault() as TextExpression;
            if (te != null && te.text.EndsWith('{')) { te.text = te.text.Remove(te.text.Length - 1, 1); }
            this.expressions.Add(new CalculatedExpression { text = expression });
        }
        public string Compile(Node node)
        {
            List<string> args = new List<string>();

            foreach (var expression in expressions)
            {
                if (expression is TextExpression)
                {
                    var text = ((TextExpression)expression).text;
                    var splits = text.Split(" ");
                    foreach (var split in splits)
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
            if (commandName == "stop")
            {
                var sb = new StringBuilder();
                sb.AppendLine("runner.Stop();");
                sb.AppendLine("return;");
                return sb.ToString();
            }
            else if (commandName == "wait")
            {
                var waitContinueLabel = node.RegisterLabel("waitContinue");
                var sb = new StringBuilder();
                sb.AppendLine($"\t\t\trunner.StartTimer({args[0]}, {waitContinueLabel});");
                sb.AppendLine("\t\t\treturn;");
                sb.AppendLine($"\t\tcase {waitContinueLabel}:\n");
                return sb.ToString();
            }
            // TODO: Should probably put registered functions somewhere other than runner.variables
            var result = $"\t\t{commandName}({string.Join(", ", args)});\n\n";
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
            // TODO deal with string variables 
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
    internal class OnceIsSeen : Step
    {
        public string variableName = "";

        public string Compile(Node node)
        {

            var result = $"\t\t\trunner.SetOnce(OnceKey::{variableName});\n";
            return result;
        }
    }

    internal class FinishNode : Step
    {
        public string Compile(Node node)
        {
            var result = "\t\t\trunner.EndNode();\n\t\t\treturn;\n\n";
            return result;
        }
    }

    internal class GoTo : Step
    {
        public string targetLabel = "";
        public string Compile(Node node)
        {
            var result = $"\t\t\trunner.ReturnAndGoto({targetLabel}); return;\n";
            return result;
        }
    }
    internal class Label : Step
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
        public int index = 0;
        public string? lineID = null;
        public string jumpToLabel = "";
        public List<string> conditions = new List<string>();
        public string? onceLabel = null;
        public int complexityCount = 0;
        public bool isLineGroupItem = false;

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
            var isOnce = !string.IsNullOrEmpty(onceLabel);

            var conditionCount = conditions.Count() + (isOnce ? 1 : 0);

            if (conditionCount > 0)
            {
                sb.AppendLine($"\t\t\t\tauto trueConditionCount = 0;");
                foreach (var condition in conditions)
                {
                    sb.AppendLine($"\t\t\t\tif({condition}){{trueConditionCount++;}}");
                }
                if (isOnce)
                {
                    sb.AppendLine($"\t\t\t\tif(!runner.Once(OnceKey::{onceLabel})){{trueConditionCount++;}}");
                }
                sb.AppendLine($"\t\t\t\tcurrentOption.condition = trueConditionCount == {conditionCount};");
                sb.AppendLine($"\t\t\t\tcurrentOption.trueConditionCount = trueConditionCount;");
                sb.AppendLine($"\t\t\t\tcurrentOption.conditionCount = {conditionCount};");
                sb.AppendLine($"\t\t\t\tcurrentOption.complexity = {this.complexityCount};");
            }
            var expressionsWithMarkup = Markup.ExtractMarkup(expressions, isLine: false, node.compiler);
            if (expressionsWithMarkup.OfType<Markup>().Any() || tags.Any())
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
                else if (expression is Select select)
                {
                    sb.AppendLine(select.getText());
                }
            });
            if (tags.Any())
            {
                var joinedTags = string.Join(", ", tags.Select(t => $"LineTag::{t.Name}"));
                sb.AppendLine($"\t\t\t\tfor(LineTag p : {{{joinedTags}}}) {{ optionMarkup.tags.emplace_back(p);}}");
                if (tags.Any(t => t.Params.Any()))
                {
                    var joinedTagParams = string.Join(", ", tags.SelectMany(t => t.Params));
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
    internal class StartOptions : Step
    {
        public string Compile(Node node)
        {
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
    internal class SendLineGroup : Step
    {
        public string NoValidOptionsJumpTo = "";
        public string Compile(Node node)
        {
            var result = $"\t\t\trunner.SetNoValidOption(&{NoValidOptionsJumpTo});\n";
            result += "\t\t\trunner.state = LineGroup;\n\t\t\treturn;\n\n";
            return result;
        }
    }
    internal class PluralMarkup : Expression
    {
        bool isCardinal = true; // if false then is ordinal
        bool isLine = true;
        string matchValue = "";
        string? Zero;
        string? One;
        string? Two;
        string? Few;
        string? Many;
        string? Other;
        public PluralMarkup(Markup markup, bool isLine, Compiler compiler)
        {
            this.isCardinal = markup.name == "plural";
            this.isLine = isLine;
            var valueIndex = markup.parameterNames.FindIndex(t => t == "value");

            matchValue = markup.parameters[valueIndex].Trim('"').Trim('{', '}');
            markup.parameters.RemoveAt(valueIndex);
            markup.parameterNames.RemoveAt(valueIndex);

            for (int i = 0; i < markup.parameters.Count(); i++)
            {
                var name = markup.parameterNames[i];
                string paramValue = markup.parameters[i].Trim('"').Trim('{', '}'); ;
                switch (name)
                {
                    case "zero": Zero = paramValue; break;
                    case "one": One = paramValue; break;
                    case "two": Two = paramValue; break;
                    case "few": Few = paramValue; break;
                    case "many": Many = paramValue; break;
                    case "other": Other = paramValue; break;
                    default: break;
                }
            }
        }
        protected void ExtractPlaceholder(string text, string pluralCase, string tabs, StringBuilder sb)
        {
            var splitPattern = "(%)";
            var subExpressions = Regex.Split(text, splitPattern).Where(t => !string.IsNullOrEmpty(t)).Select(t => t == "%" ? matchValue : $"\"{t}\"");
            sb.AppendLine($"{tabs}\tcase PluralCase::{pluralCase}: ");
            foreach (var chunk in subExpressions)
            {
                if (isLine)
                {
                    sb.AppendLine($"{tabs}\t\tcurrentLine << {chunk};");
                }
                else
                {
                    sb.Append($"{tabs}\t\tcurrentOption << {chunk};");
                }
            }
            sb.AppendLine($"{tabs}\t\tbreak;");
        }
        public string getText()
        {
            var tabs = isLine ? "\t\t\t" : "\t\t\t\t";
            var sb = new StringBuilder();

            sb.AppendLine($"{tabs}switch({(isCardinal ? "GetCardinalPluralCase" : "GetOrdinalPluralCase")}({matchValue})) {{");
            if (!string.IsNullOrEmpty(Zero)) { ExtractPlaceholder(Zero, "Zero", tabs, sb); }
            if (!string.IsNullOrEmpty(One)) { ExtractPlaceholder(One, "One", tabs, sb); }
            if (!string.IsNullOrEmpty(Two)) { ExtractPlaceholder(Two, "Two", tabs, sb); }
            if (!string.IsNullOrEmpty(Few)) { ExtractPlaceholder(Few, "Few", tabs, sb); }
            if (!string.IsNullOrEmpty(Many)) { ExtractPlaceholder(Many, "Many", tabs, sb); }
            if (!string.IsNullOrEmpty(Other)) { ExtractPlaceholder(Other, "Other", tabs, sb); }


            sb.AppendLine($"{tabs}\tdefault: break;");
            sb.AppendLine($"{tabs}}}");
            return sb.ToString();
        }
    }
    internal class Select : Expression
    {
        Dictionary<string, string> mapping = new Dictionary<string, string>();
        string matchValue = "";
        bool isLine = true;
        bool isNumberKeys = false;
        public Select(Markup markup, bool isLine, Compiler compiler)
        {
            this.isLine = isLine;
            var valueIndex = markup.parameterNames.FindIndex(t => t == "value");

            matchValue = markup.parameters[valueIndex].Trim('"').Trim('{', '}');
            markup.parameters.RemoveAt(valueIndex);
            markup.parameterNames.RemoveAt(valueIndex);

            if (markup.parameterNames.All(p => int.TryParse(p, out _) || p.Contains("::")))
            {
                isNumberKeys = true;
            }

            for (int i = 0; i < markup.parameters.Count(); i++)
            {
                var name = markup.parameterNames[i];
                name = name.Contains("::") ? $"(int){name}" : name;
                mapping.Add(name, markup.parameters[i]);
            }
        }
        public string getText()
        {
            var tabs = isLine ? "\t\t\t" : "\t\t\t\t";
            var sb = new StringBuilder();

            sb.AppendLine($"{tabs}switch({(isNumberKeys ? matchValue : $"bn::make_hash({matchValue})")}) {{");
            foreach (var map in mapping)
            {
                sb.Append($"{tabs}\tcase {(isNumberKeys ? map.Key : $"\"{map.Key}\"_h")}: ");
                if (isLine)
                {
                    sb.Append($"currentLine << {map.Value}; break;\n");
                }
                else
                {
                    sb.Append($"currentOption << {map.Value}; break; \n");
                }
            }
            sb.AppendLine($"{tabs}\tdefault: break;");
            sb.AppendLine($"{tabs}}}");
            return sb.ToString();
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
                ?.Where(p => !string.IsNullOrWhiteSpace(p))
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
    internal class Enum
    {
        public required string Name { get; set; }
        public List<EnumCase> Cases = new List<EnumCase>();
        public string Compile()
        {
            if (!Cases.Any())
            {
                return "";
            }
            var sb = new StringBuilder();
            var firstValue = Cases[0].Value;
            if (firstValue == null)
            {
                var joinedCaseNames = string.Join(", ", Cases.Select(c => c.Name));
                sb.AppendLine($"\tenum class {Name} : int {{ {joinedCaseNames} }};");
            }
            else if (int.TryParse(firstValue, out _))
            {
                var joinedCaseItems = string.Join(", ", Cases.Select(c => $"{c.Name} = {c.Value}"));
                sb.AppendLine($"\tenum class {Name} : int {{ {joinedCaseItems} }};");
            }
            // Apparently invalid to have a fixed point number as an enum value
            //else if (firstValue.StartsWith("bn::fixed"))
            //{
            //    var joinedStructDefs = string.Join("", Cases.Select(c => $"\n\t\tconstexpr static bn::fixed {c.Name} = {c.Value};"));
            //    sb.AppendLine($"\tstruct _{Name} {{{joinedStructDefs}}};");
            //    sb.AppendLine($"\tconstexpr _{Name} {Name}");
            //}
            else
            {
                var joinedStructDefs = string.Join("", Cases.Select(c => $"\n\t\tconstexpr static bn::string_view {c.Name} = {c.Value};"));
                sb.AppendLine($"\tstruct _{Name} {{{joinedStructDefs}}};");
                sb.AppendLine($"\tconstexpr _{Name} {Name}");
            }

            return sb.ToString();
        }
    }

    internal class EnumCase
    {
        public required string Name { get; set; }
        public string? Value { get; set; }

    }

    internal class SmartVariable
    {
        public required string Name { get; set; }
        public HashSet<string> Dependencies = new HashSet<string>();
        public ExpressionContext? Expression { get; set; }
    }
}
