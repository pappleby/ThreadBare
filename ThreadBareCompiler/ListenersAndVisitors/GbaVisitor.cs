using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using global::Yarn.Compiler;
using static Yarn.Compiler.YarnSpinnerParser;

namespace ThreadBare
{
    internal class GbaVisitor : YarnSpinnerParserBaseVisitor<int>
    {
        protected Compiler compiler;

        internal string? trackingEnabled = null;
        protected ExpressionsVisitor expressionVisitor;
        public GbaVisitor(Compiler compiler, string trackingEnabled)
        {
            this.compiler = compiler;
            this.trackingEnabled = trackingEnabled;
            this.expressionVisitor = new ExpressionsVisitor(compiler, trackingEnabled);
        }

        private int GenerateCodeForExpressionsInFormattedText(IList<IParseTree> nodes)
        {
            int expressionCount = 0;

            // First, visit all of the nodes, which are either terminal text
            // nodes or expressions. if they're expressions, we evaluate them,
            // and inject a positional reference into the final string.
            foreach (var child in nodes)
            {
                var currentLine = this.compiler.CurrentNode?.GetCurrentLine();
                if (child is ITerminalNode)
                {
                    // nothing to do; string assembly will have been done by the
                    // StringTableGeneratorVisitor
                    currentLine?.AddTextExpression(child.GetText());
                }
                else if (child is ParserRuleContext)
                {
                    // assume that this is an expression (the parser only
                    // permits them to be expressions, but we can't specify that
                    // here) - visit it, and we will emit code that pushes the
                    // final value of this expression onto the stack. running
                    // the line will pop these expressions off the stack.
                    expressionVisitor.Visit(child);
                    expressionCount += 1;
                    var exp = expressionVisitor.FlushParamaters() ?? "-999";
                    currentLine?.AddCalculatedExpression(exp);

                }
            }

            return expressionCount;
        }
        static int l = 0;
        // a regular ol' line of text
        public override int VisitLine_statement(YarnSpinnerParser.Line_statementContext context)
        {
            // Get the lineID for this string from the hashtags
            var lineIDTag = Compiler.GetLineIDTag(context.hashtag());
            string lineID;
            if (lineIDTag == null)
            {
                lineID = "line" + l.ToString();
                l++;
            }
            else
            {
                lineID = lineIDTag.text.Text;
            }
            var outputLine = new Line { lineID = lineID };
            var cn = this.compiler.CurrentNode;

            // This should be split up into individual expressions, but good enough to start
            this.compiler.CurrentNode?.AddStep(outputLine);
            var expressionCount = this.GenerateCodeForExpressionsInFormattedText(context.line_formatted_text().children);
            base.VisitLine_statement(context);
            var condition = context.line_condition();
            ExpressionContext conditionExpressionContext;
            if (!(condition?.IsEmpty ?? true))
            {
                if (condition is LineOnceConditionContext lineOnceConditionContext)
                {
                    outputLine.once = true;
                    conditionExpressionContext = lineOnceConditionContext.expression();
                }
                else if (condition is LineConditionContext lineConditionContext)
                {
                    conditionExpressionContext = lineConditionContext.expression();
                }
                else
                {
                    throw new System.ArgumentException("Unknown line condition type");
                }
                if (conditionExpressionContext != null)
                {
                    // Evaluate the condition, and leave it on the stack
                    expressionVisitor.Visit(conditionExpressionContext);
                    outputLine.condition = expressionVisitor.parameters.Pop();
                }
            }
            expressionVisitor.FlushParamaters();
            return 0;
        }

        public override int VisitHashtag([NotNull] YarnSpinnerParser.HashtagContext context)
        {
            var tagText = context.HASHTAG_TEXT().GetText();
            var currentLine = this.compiler.CurrentNode?.GetCurrentLine() as Line;
            currentLine?.AddTag(this.compiler, tagText);
            return 0;
        }

        // A set command: explicitly setting a value to an expression <<set $foo
        // to 1>>
        public override int VisitSet_statement([NotNull] YarnSpinnerParser.Set_statementContext context)
        {
            var variable = context.variable().GetText().Replace("$", "");


            var setStep = new Set { variable = variable };
            switch (context.op.Type)
            {
                case YarnSpinnerLexer.OPERATOR_ASSIGNMENT:
                    break;
                case YarnSpinnerLexer.OPERATOR_MATHS_ADDITION_EQUALS:
                    setStep.operation = "+=";
                    break;
                case YarnSpinnerLexer.OPERATOR_MATHS_SUBTRACTION_EQUALS:
                    setStep.operation = "-=";
                    break;
                case YarnSpinnerLexer.OPERATOR_MATHS_MULTIPLICATION_EQUALS:
                    setStep.operation = "*=";
                    break;
                case YarnSpinnerLexer.OPERATOR_MATHS_DIVISION_EQUALS:
                    setStep.operation = "/=";
                    break;
                case YarnSpinnerLexer.OPERATOR_MATHS_MODULUS_EQUALS:
                    setStep.operation = "%=";
                    break;
            }
            this.compiler.CurrentNode?.AddStep(setStep);
            expressionVisitor.Visit(context.expression());
            setStep.expression = expressionVisitor.FlushParamaters() ?? "-999";
            return 0;
        }

        public override int VisitCall_statement(YarnSpinnerParser.Call_statementContext context)
        {
            // TODO check if this ever gets called
            // Visit our function call, which will invoke the function
            this.Visit(context.function_call());
            return 0;
        }

        public override int VisitDetourToExpression([NotNull] YarnSpinnerParser.DetourToExpressionContext context)
        {
            // could probably do this by getting the hash of the expression result and using that in a node hash -> node function map (same as jump to expression)
            throw new System.NotImplementedException();
        }

        public override int VisitDetourToNodeName([NotNull] YarnSpinnerParser.DetourToNodeNameContext context)
        {
            var cn = this.compiler.CurrentNode;
            var destination = context.destination.Text;
            var d = new Detour();
            d.Target = destination;
            cn?.AddStep(d);
            return 0;
        }

        // semi-free form text that gets passed along to the game for things
        // like <<turn fred left>> or <<unlockAchievement FacePlant>>
        public override int VisitCommand_statement(YarnSpinnerParser.Command_statementContext context)
        {
            var cn = this.compiler.CurrentNode;

            var c = new Command();

            foreach (var node in context.command_formatted_text().children)
            {
                if (node is ITerminalNode)
                {
                    c.AddTextExpression(node.GetText());
                }
                else if (node is ParserRuleContext)
                {
                    // Generate code for evaluating the expression at runtime
                    expressionVisitor.Visit(node);

                    var expression = expressionVisitor.FlushParamaters();

                    c.AddCalculatedExpression(expression);
                }
            }

            cn?.AddStep(c);

            return 0;
        }

        // emits the required bytecode for the function call


        // if statement ifclause (elseifclause)* (elseclause)? <<endif>>
        public override int VisitIf_statement(YarnSpinnerParser.If_statementContext context)
        {

            //// label to give us a jump point for when the if finishes
            var endOfIfStatementLabel = this.compiler.CurrentNode?.RegisterLabel() ?? "";

            //// handle the if
            var ifClause = context.if_clause();
            this.GenerateClause(endOfIfStatementLabel, ifClause.statement(), ifClause.expression());

            // all elseifs
            foreach (var elseIfClause in context.else_if_clause())
            {
                this.GenerateClause(endOfIfStatementLabel, elseIfClause.statement(), elseIfClause.expression());
            }

            // the else, if there is one
            var elseClause = context.else_clause();
            if (elseClause != null)
            {
                this.GenerateClause(endOfIfStatementLabel, elseClause.statement(), null);
            }
            this.compiler.CurrentNode?.AddStep(new Label { label = endOfIfStatementLabel });

            return 0;
        }

        public override int VisitOnce_statement([NotNull] Once_statementContext context)
        {
            //// label to give us a jump point for when the once finishes
            var endOfOnceStatementLabel = this.compiler.CurrentNode!.RegisterLabel();

            // TODO: Handle the once part of the variable (should be a mostly stable (either base on tag or how many once statements into the current node there are))
            var onceVariableName = this.compiler.RegisterOnceVariable();
            var once_condition_expression = context.once_primary_clause().expression();

            var clauseStatement = context.once_primary_clause().statement();
            this.GenerateClause(endOfOnceStatementLabel, clauseStatement, once_condition_expression, onceVariableName);

            // the else, if there is one
            var elseClause = context.once_alternate_clause()?.statement();
            if (elseClause != null)
            {
                this.GenerateClause(endOfOnceStatementLabel, elseClause, null);
            }

            this.compiler.CurrentNode?.AddStep(new Label { label = endOfOnceStatementLabel });

            return 0;
        }

        private void GenerateClause(string jumpLabel, StatementContext[] children, ExpressionContext? expression, string? onceVariableName = null)
        {
            var cn = this.compiler.CurrentNode!;
            string endOfClauseLabel = this.compiler.CurrentNode?.RegisterLabel("skipclause") ?? "";

            var testExpression = "";
            // handling the expression (if it has one) will only be called on
            // ifs, elseifs, and once's with a condition
            if (expression != null)
            {
                // Code-generate the expression
                expressionVisitor.Visit(expression);

                var ps = expressionVisitor.parameters;
                testExpression += expressionVisitor.FlushParamaters();

            }
            if (!string.IsNullOrEmpty(onceVariableName))
            {
                testExpression = $"!runner.Once(OnceKey::{onceVariableName})" + (expression != null ? $"&&{testExpression}" : "");
            }
            if (!string.IsNullOrEmpty(testExpression))
            {
                var clause = new If { expression = testExpression, jumpIfFalseLabel = endOfClauseLabel };
                cn.AddStep(clause);
            }

            if (!string.IsNullOrEmpty(onceVariableName))
            {
                cn.AddStep(new OnceIsSeen { variableName = onceVariableName });
            }
            // running through all of the children statements
            foreach (var child in children)
            {
                this.Visit(child);
            }

            var finishClause = new GoTo { targetLabel = jumpLabel };
            this.compiler.CurrentNode?.AddStep(finishClause);
            if (expression != null)
            {
                this.compiler.CurrentNode?.AddStep(new Label { label = endOfClauseLabel });
            }
        }


        public override int VisitLine_group_statement(YarnSpinnerParser.Line_group_statementContext context)
        {
            // Idea: make a bunch of options without any text in them
            // move the text for each line group item into the jump to

            // instead of returning to the user, have the runner pick the option. 
            // Possibly need to add complexity score to each option 
            // and maybe store the count of how many times each option was picked? (is there a way to make this not a nightmare)

            var cn = this.compiler.CurrentNode!;
            string endOfGroupLabel = cn.RegisterLabel("group_end");

            var labels = new List<string>();
            var onceLabels = new Dictionary<int, string>();
            int optionCount = 0;

            cn.AddStep(new StartOptions());

            foreach (var shortcut in context.line_group_item())
            {
                var optionStep = new Option { index = optionCount, isLineGroupItem = true };
                cn.AddStep(optionStep);

                var lineStatement = shortcut.line_statement();

                // Generate the name of internal label that we'll jump to if
                // this option is selected. We'll emit the label itself later.
                string optionDestinationLabel = this.compiler.CurrentNode?.RegisterLabel($"shortcutoption_{this.compiler.CurrentNode.Name ?? "node"}_{optionCount + 1}") ?? "";
                labels.Add(optionDestinationLabel);
                optionStep.jumpToLabel = optionDestinationLabel;

                // This line statement may have a condition on it. If it does,
                // emit code that evaluates the condition, and add a flag on the
                // 'Add Option' instruction that indicates that a condition
                // exists.
                var condition = shortcut.line_statement().line_condition();
                string? onceVariableName = null;
                if (!(condition?.IsEmpty ?? true))
                {
                    var conditionCount = 0;

                    ExpressionContext conditionExpressionContext;

                    if (condition is LineOnceConditionContext lineOnceConditionContext)
                    {
                        conditionCount += 1;
                        onceVariableName = this.compiler.RegisterOnceVariable();
                        optionStep.onceLabel = onceVariableName;
                        onceLabels[labels.Count - 1] = onceVariableName;
                        conditionExpressionContext = lineOnceConditionContext.expression();
                    }
                    else if (condition is LineConditionContext lineConditionContext)
                    {
                        conditionExpressionContext = lineConditionContext.expression();
                    }
                    else
                    {
                        throw new ArgumentException("Unknown line condition type");
                    }

                    if (conditionExpressionContext != null)
                    {
                        conditionCount += ExpressionsVisitor.GetBooleanOperatorCountInExpression(conditionExpressionContext);
                        optionStep.complexityCount = conditionCount;
                        // Evaluate the condition, and leave it on the stack
                        expressionVisitor.Visit(conditionExpressionContext);

                        optionStep.conditions.Add(expressionVisitor.FlushParamaters());
                    }
                }
                // Store hashtags somewhere? handle later?
                optionCount++;
            }

            cn.AddStep(new SendLineGroup { NoValidOptionsJumpTo = endOfGroupLabel });

            // We'll now emit the labels and code associated with each option.
            optionCount = 0;
            foreach (var shortcut in context.line_group_item())
            {
                // Emit the label for this option's code
                cn.AddStep(new Label { label = labels[optionCount] });

                if (onceLabels.TryGetValue(optionCount, out var onceLabel))
                {
                    cn.AddStep(new OnceIsSeen { variableName = onceLabel });
                }
                var hashtags = shortcut.line_statement().hashtag() ?? [];
                var lineIDTag = Compiler.GetLineIDTag(hashtags);
                string lineID;
                if (lineIDTag == null)
                {
                    lineID = "line" + l.ToString();
                    l++;
                }
                else
                {
                    lineID = lineIDTag.text.Text;
                }
                var outputLine = new Line { lineID = lineID };
                foreach (var hashtag in hashtags)
                {
                    var tag = hashtag.HASHTAG_TEXT().GetText();
                    outputLine.AddTag(this.compiler, tag);
                }

                // This should be split up into individual expressions, but good enough to start
                cn.AddStep(outputLine);
                this.GenerateCodeForExpressionsInFormattedText(shortcut.line_statement().line_formatted_text().children);
                // Run through all the children statements of the shortcut
                // option.
                foreach (var child in shortcut.statement())
                {
                    this.Visit(child);
                }

                // Jump to the end of this shortcut option group.
                cn.AddStep(new GoTo { targetLabel = endOfGroupLabel });

                optionCount++;
            }

            // We made it to the end! Mark the end of the group, so we can jump
            // to it.
            cn.AddStep(new Label { label = endOfGroupLabel });

            return 0;
        }


        // for the shortcut options (-> line of text <<if expression>> indent
        // statements dedent)+
        public override int VisitShortcut_option_statement(YarnSpinnerParser.Shortcut_option_statementContext context)
        {
            string endOfGroupLabel = this.compiler.CurrentNode?.RegisterLabel("group_end") ?? "";

            var labels = new List<string>();
            var onceLabels = new Dictionary<int, string>();
            int optionCount = 0;
            var cn = this.compiler.CurrentNode!;

            cn.AddStep(new StartOptions());

            // For each option, create an internal destination label that, if
            // the user selects the option, control flow jumps to. Then,
            // evaluate its associated line_statement, and use that as the
            // option text. Finally, add this option to the list of upcoming
            // options.
            foreach (var shortcut in context.shortcut_option())
            {
                var optionStep = new Option { index = optionCount };
                cn.AddStep(optionStep);

                // Generate the name of internal label that we'll jump to if
                // this option is selected. We'll emit the label itself later.
                string optionDestinationLabel = this.compiler.CurrentNode?.RegisterLabel($"shortcutoption_{this.compiler.CurrentNode.Name ?? "node"}_{optionCount + 1}") ?? "";
                labels.Add(optionDestinationLabel);
                optionStep.jumpToLabel = optionDestinationLabel;

                // This line statement may have a condition on it. If it does,
                // emit code that evaluates the condition, and add a flag on the
                // 'Add Option' instruction that indicates that a condition
                // exists.
                var condition = shortcut.line_statement().line_condition();
                string? onceVariableName = null;
                if (!(condition?.IsEmpty ?? true))
                {
                    ExpressionContext conditionExpressionContext;

                    if (condition is LineOnceConditionContext lineOnceConditionContext)
                    {
                        onceVariableName = this.compiler.RegisterOnceVariable();
                        optionStep.onceLabel = onceVariableName;
                        onceLabels[labels.Count - 1] = onceVariableName;
                        conditionExpressionContext = lineOnceConditionContext.expression();
                    }
                    else if (condition is LineConditionContext lineConditionContext)
                    {
                        conditionExpressionContext = lineConditionContext.expression();
                    }
                    else
                    {
                        throw new ArgumentException("Unknown line condition type");
                    }

                    if (conditionExpressionContext != null)
                    {
                        // Evaluate the condition, and leave it on the stack
                        expressionVisitor.Visit(conditionExpressionContext);

                        optionStep.conditions.Add(expressionVisitor.FlushParamaters());
                    }
                }
                foreach (var hashtag in shortcut.line_statement()?.hashtag() ?? [])
                {
                    var tag = hashtag.HASHTAG_TEXT().GetText();
                    optionStep.AddTag(this.compiler, tag);
                }

                // We can now prepare and add the option.

                // Start by figuring out the text that we want to add. This will
                // involve evaluating any inline expressions.
                this.GenerateCodeForExpressionsInFormattedText(shortcut.line_statement().line_formatted_text().children);

                // Get the line ID from the hashtags if it has one
                var lineIDTag = Compiler.GetLineIDTag(shortcut.line_statement().hashtag());
                string lineID = lineIDTag?.text?.Text ?? this.compiler.CurrentNode?.RegisterLabel("OptionLine") ?? "";

                if (lineIDTag != null)
                {
                    optionStep.lineID = lineID;
                }

                optionCount++;
            }

            // All of the options that we intend to show are now ready to go.
            cn.AddStep(new SendOptions());


            // We'll now emit the labels and code associated with each option.
            optionCount = 0;
            foreach (var shortcut in context.shortcut_option())
            {
                // Emit the label for this option's code
                cn.AddStep(new Label { label = labels[optionCount] });

                if (onceLabels.TryGetValue(optionCount, out var onceLabel))
                {
                    cn.AddStep(new OnceIsSeen { variableName = onceLabel });
                }

                // Run through all the children statements of the shortcut
                // option.
                foreach (var child in shortcut.statement())
                {
                    this.Visit(child);
                }

                // Jump to the end of this shortcut option group.
                cn.AddStep(new GoTo { targetLabel = endOfGroupLabel });

                optionCount++;
            }



            // We made it to the end! Mark the end of the group, so we can jump
            // to it.
            cn.AddStep(new Label { label = endOfGroupLabel });
            return 0;
        }

        public override int VisitDeclare_statement(YarnSpinnerParser.Declare_statementContext context)
        {
            // Declare statements do not participate in code generation
            // TODO: but they probably should, at least to get default values, right?
            return 0;
        }

        // A <<jump>> command, which immediately jumps to another node, given
        // its name.
        public override int VisitJumpToNodeName([NotNull] YarnSpinnerParser.JumpToNodeNameContext context)
        {
            var outputJumpStep = new Jump { Target = context.destination.Text };
            compiler.CurrentNode?.AddStep(outputJumpStep);

            return 0;
        }

        // A <<jump>> command, which immediately jumps to another node, given an
        // expression that resolves to a node's name.
        public override int VisitJumpToExpression([NotNull] YarnSpinnerParser.JumpToExpressionContext context)
        {
            // Really don't want to allow this considering that required a string table of all the node names and string comparison whenever this evaluates
            // Technically possible though :/ 
            throw new NotImplementedException();

            // Evaluate the expression, and jump to the result on the stack.
            // this.Visit(context.expression());
        }

        public override int VisitReturn_statement([NotNull] Return_statementContext context)
        {
            var outputReturnStep = new FinishNode { };
            compiler.CurrentNode!.AddStep(outputReturnStep);
            return 0;
        }
    }

}
