using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using global::Yarn.Compiler;
using global::Yarn;
using static Antlr4.Runtime.Atn.SemanticContext;
using static Yarn.Instruction.Types;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ThreadBare
{
    internal class GbaVisitor : YarnSpinnerParserBaseVisitor<int>
    {
        private Compiler compiler;

        internal string? trackingEnabled = null;

        public GbaVisitor(Compiler compiler, string trackingEnabled)
        {
            this.compiler = compiler;
            this.trackingEnabled = trackingEnabled;
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
                    this.Visit(child);
                    expressionCount += 1;
                    var exp = this.compiler.CurrentNode?.FlushParamaters() ?? "-999";
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
            // This should be split up into individual expressions, but good enough to start
            this.compiler.CurrentNode?.AddStep(outputLine);
            var expressionCount = this.GenerateCodeForExpressionsInFormattedText(context.line_formatted_text().children);

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
            this.Visit(context.expression());
            setStep.expression = this.compiler.CurrentNode?.FlushParamaters() ?? "-999";
            return 0;
        }

        public override int VisitCall_statement(YarnSpinnerParser.Call_statementContext context)
        {
            // Visit our function call, which will invoke the function
            this.Visit(context.function_call());
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
                    this.Visit(node);

                    var expression = cn?.parameters?.Pop();
                    cn?.FlushParamaters();

                    c.AddCalculatedExpression(expression);
                }
            }

            cn?.AddStep(c);
            return 0;
        }

        // emits the required bytecode for the function call
        private void GenerateCodeForFunctionCall(string functionName, YarnSpinnerParser.Function_callContext functionContext, YarnSpinnerParser.ExpressionContext[] parameters)
        {
            var compiledParameters = new List<string>();
            var ps = this.compiler.CurrentNode?.parameters;
            // generate the instructions for all of the parameters
            foreach (var parameter in parameters)
            {
                this.Visit(parameter);
                var p = ps?.Pop();
                if (!string.IsNullOrEmpty(p)) { compiledParameters.Add(p); }
            }

            var outputText = $"{functionName}({string.Join(", ", compiledParameters)})";
            ps?.Push(outputText);
        }

        // handles emiting the correct instructions for the function
        public override int VisitFunction_call(YarnSpinnerParser.Function_callContext context)
        {
            string functionName = context.FUNC_ID().GetText();

            this.GenerateCodeForFunctionCall(functionName, context, context.expression());

            return 0;
        }

        // if statement ifclause (elseifclause)* (elseclause)? <<endif>>
        public override int VisitIf_statement(YarnSpinnerParser.If_statementContext context)
        {

            //// label to give us a jump point for when the if finishes
            var endOfIfStatementLabel = this.compiler.RegisterLabel();

            //// handle the if
            var ifClause = context.if_clause();
            this.GenerateClause(endOfIfStatementLabel, ifClause, ifClause.statement(), ifClause.expression());

            // all elseifs
            foreach (var elseIfClause in context.else_if_clause())
            {
                this.GenerateClause(endOfIfStatementLabel, elseIfClause, elseIfClause.statement(), elseIfClause.expression());
            }

            // the else, if there is one
            var elseClause = context.else_clause();
            if (elseClause != null)
            {
                this.GenerateClause(endOfIfStatementLabel, elseClause, elseClause.statement(), null);
            }
            this.compiler.CurrentNode?.AddStep(new Label { label = endOfIfStatementLabel });

            return 0;
        }

        private void GenerateClause(string jumpLabel, ParserRuleContext clauseContext, YarnSpinnerParser.StatementContext[] children, YarnSpinnerParser.ExpressionContext expression)
        {

            string endOfClauseLabel = this.compiler.RegisterLabel("skipclause");

            // handling the expression (if it has one) will only be called on
            // ifs and elseifs
            if (expression != null)
            {
                // Code-generate the expression
                this.Visit(expression);
                var cn = this.compiler.CurrentNode;
                var ps = cn?.parameters;
                var testExpression = ps?.Pop() ?? "true";
                cn?.FlushParamaters();

                var clause = new If { expression = testExpression, jumpIfFalseLabel = endOfClauseLabel };
                cn?.AddStep(clause);
            }

            // running through all of the children statements
            foreach (var child in children)
            {
                this.Visit(child);
            }


            var finishClause = new GoTo { targetLabel = jumpLabel };
            this.compiler.CurrentNode?.AddStep(finishClause);

            this.compiler.CurrentNode?.AddStep(new Label { label = endOfClauseLabel });
        }

        // for the shortcut options (-> line of text <<if expression>> indent
        // statements dedent)+
        public override int VisitShortcut_option_statement(YarnSpinnerParser.Shortcut_option_statementContext context)
        {
            string endOfGroupLabel = this.compiler.RegisterLabel("group_end");

            var labels = new List<string>();

            int optionCount = 0;
            var cn = this.compiler.CurrentNode;

            cn?.AddStep(new StartOptions());

            // For each option, create an internal destination label that, if
            // the user selects the option, control flow jumps to. Then,
            // evaluate its associated line_statement, and use that as the
            // option text. Finally, add this option to the list of upcoming
            // options.
            foreach (var shortcut in context.shortcut_option())
            {
                var optionStep = new Option{ index= optionCount };
                cn?.AddStep(optionStep);

                // Generate the name of internal label that we'll jump to if
                // this option is selected. We'll emit the label itself later.
                string optionDestinationLabel = this.compiler.RegisterLabel($"shortcutoption_{this.compiler.CurrentNode.Name ?? "node"}_{optionCount + 1}");
                labels.Add(optionDestinationLabel);
                optionStep.jumpToLabel = optionDestinationLabel;

                // This line statement may have a condition on it. If it does,
                // emit code that evaluates the condition, and add a flag on the
                // 'Add Option' instruction that indicates that a condition
                // exists.
                if (shortcut.line_statement()?.line_condition()?.expression() != null)
                {
                    // Evaluate the condition, and leave it on the stack
                    this.Visit(shortcut.line_statement().line_condition().expression());

                    optionStep.condition = cn?.parameters?.Pop();
                    cn?.FlushParamaters();
                }

                // We can now prepare and add the option.

                // Start by figuring out the text that we want to add. This will
                // involve evaluating any inline expressions.
                this.GenerateCodeForExpressionsInFormattedText(shortcut.line_statement().line_formatted_text().children);

                // Get the line ID from the hashtags if it has one
                var lineIDTag = Compiler.GetLineIDTag(shortcut.line_statement().hashtag());
                string lineID = lineIDTag?.text?.Text ?? this.compiler.RegisterLabel("OptionLine");

                if (lineIDTag != null)
                {
                    optionStep.lineID = lineID;
                }

                optionCount++;
            }

            // All of the options that we intend to show are now ready to go.
            cn?.AddStep(new SendOptions());


            // We'll now emit the labels and code associated with each option.
            optionCount = 0;
            foreach (var shortcut in context.shortcut_option())
            {
                // Emit the label for this option's code
                cn?.AddStep(new Label { label = labels[optionCount] });

                // Run through all the children statements of the shortcut
                // option.
                foreach (var child in shortcut.statement())
                {
                    this.Visit(child);
                }

                // Jump to the end of this shortcut option group.
                cn?.AddStep(new GoTo { targetLabel = endOfGroupLabel });

                optionCount++;
            }



            // We made it to the end! Mark the end of the group, so we can jump
            // to it.
            cn?.AddStep(new Label { label = endOfGroupLabel });
            return 0;
        }

        // the calls for the various operations and expressions first the
        // special cases (), unary -, !, and if it is just a value by itself
        #region specialCaseCalls

        // (expression)
        public override int VisitExpParens(YarnSpinnerParser.ExpParensContext context)
        {
            return this.Visit(context.expression());
        }

        // -expression
        public override int VisitExpNegative(YarnSpinnerParser.ExpNegativeContext context)
        {
            this.GenerateCodeForOperation(Operator.UnaryMinus, context.op, context.Type, context.expression());

            return 0;
        }

        // (not NOT !)expression
        public override int VisitExpNot(YarnSpinnerParser.ExpNotContext context)
        {
            this.GenerateCodeForOperation(Operator.Not, context.op, context.Type, context.expression());

            return 0;
        }

        // variable
        public override int VisitExpValue(YarnSpinnerParser.ExpValueContext context)
        {
            return this.Visit(context.value());
        }
        #endregion

        #region lValueOperatorrValueCalls

        /// <summary>
        /// Emits code that calls a method appropriate for the operator
        /// <paramref name="op"/> on the type <paramref name="type"/>, given the operands <paramref name="operands"/>.
        /// </summary>
        /// <param name="op">The operation to perform on <paramref name="operands"/>.</param>
        /// <param name="operatorToken">The first token in the statement that is responsible for this operation.</param>
        /// <param name="type">The type of the expression.</param>
        /// <param name="operands">The operands to perform the operation <paramref name="op"/> on.</param>
        /// <exception cref="InvalidOperationException">Thrown when there is no matching instructions for the <paramref name="op"/></exception>
        private void GenerateCodeForOperation(Operator op, IToken operatorToken, Yarn.IType type, params ParserRuleContext[] operands)
        {
            //// Generate code for each of the operands, so that their value is
            //// now on the stack.
            foreach (var operand in operands)
            {
                this.Visit(operand);
            }
            var ps = this.compiler.CurrentNode?.parameters;
            var apply2Op = (string o) =>
            {
                var right = ps?.Pop();
                var left = ps?.Pop();
                ps?.Push($"({left} {o} {right})");
            };
            var apply1Op = (string o) =>
            {
                var left = ps?.Pop();
                ps?.Push($"({o}{left})");
            };

            switch (op)
            {
                case Operator.LessThanOrEqualTo: apply2Op("<="); break;
                case Operator.GreaterThanOrEqualTo: apply2Op(">="); break;
                case Operator.LessThan: apply2Op("<"); break;
                case Operator.GreaterThan: apply2Op(">"); break;
                case Operator.EqualTo: apply2Op("=="); break;
                case Operator.NotEqualTo: apply2Op("!="); break;
                case Operator.Add: apply2Op("+"); break;
                case Operator.Minus: apply2Op("-"); break;
                case Operator.Multiply: apply2Op("*"); break;
                case Operator.Divide: apply2Op("/"); break;
                case Operator.Modulo: apply2Op("%"); break;
                case Operator.And: apply2Op("&&"); break;
                case Operator.Or: apply2Op("||"); break;
                case Operator.Not: apply1Op("!"); break;
                case Operator.UnaryMinus: apply1Op("-"); break;
                case Operator.None: break;
                case Operator.Xor:
                    {
                        var right = ps?.Pop();
                        var left = ps?.Pop();
                        ps?.Push($"(!{left} != !{right})");
                        break;
                    }


            }
            return;
        }

        // * / %
        public override int VisitExpMultDivMod(YarnSpinnerParser.ExpMultDivModContext context)
        {
            this.GenerateCodeForOperation(TokensToOperators[context.op.Type], context.op, context.Type, context.expression(0), context.expression(1));
            return 0;
        }

        // + -
        public override int VisitExpAddSub(YarnSpinnerParser.ExpAddSubContext context)
        {
            this.GenerateCodeForOperation(TokensToOperators[context.op.Type], context.op, context.Type, context.expression(0), context.expression(1));
            return 0;
        }

        // < <= > >=
        public override int VisitExpComparison(YarnSpinnerParser.ExpComparisonContext context)
        {
            this.GenerateCodeForOperation(TokensToOperators[context.op.Type], context.op, context.Type, context.expression(0), context.expression(1));
            return 0;
        }

        // == !=
        public override int VisitExpEquality(YarnSpinnerParser.ExpEqualityContext context)
        {
            this.GenerateCodeForOperation(TokensToOperators[context.op.Type], context.op, context.Type, context.expression(0), context.expression(1));
            return 0;
        }

        // and && or || xor ^
        public override int VisitExpAndOrXor(YarnSpinnerParser.ExpAndOrXorContext context)
        {
            this.GenerateCodeForOperation(TokensToOperators[context.op.Type], context.op, context.Type, context.expression(0), context.expression(1));
            return 0;
        }
        #endregion

        // the calls for the various value types this is a wee bit messy but is
        // easy to extend, easy to read and requires minimal checking as ANTLR
        // has already done all that does have code duplication though
        #region valueCalls
        public override int VisitValueVar(YarnSpinnerParser.ValueVarContext context)
        {
            return this.Visit(context.variable());
        }

        public override int VisitValueNumber(YarnSpinnerParser.ValueNumberContext context)
        {
            // may need to reevaluate non integer floats
            float number = float.Parse(context.NUMBER().GetText(), CultureInfo.InvariantCulture);
            this.compiler.CurrentNode?.AddParameter(number.ToString());
            return 0;
        }

        public override int VisitValueTrue(YarnSpinnerParser.ValueTrueContext context)
        {
            this.compiler.CurrentNode?.AddParameter("true");
            return 0;
        }

        public override int VisitValueFalse(YarnSpinnerParser.ValueFalseContext context)
        {
            this.compiler.CurrentNode?.AddParameter("false");
            return 0;
        }

        public override int VisitVariable(YarnSpinnerParser.VariableContext context)
        {
            string variableName = context.VAR_ID().GetText().Replace("$", "runner.variables.");
            this.compiler.CurrentNode?.AddParameter(variableName);

            return 0;
        }

        public override int VisitValueString(YarnSpinnerParser.ValueStringContext context)
        {
            // stripping the " off the front and back actually is this what we
            // want?
            string stringVal = context.STRING().GetText();// .Trim('"');

            this.compiler.CurrentNode?.AddParameter(stringVal);
            return 0;
        }

        // all we need do is visit the function itself, it will handle
        // everything
        public override int VisitValueFunc(YarnSpinnerParser.ValueFuncContext context)
        {
            this.Visit(context.function_call());
            return 0;
        }

        // null value
        public override int VisitValueNull(YarnSpinnerParser.ValueNullContext context)
        {
            // shouldn't have any nulls
            throw new NotImplementedException();
        }
        #endregion

        public override int VisitDeclare_statement(YarnSpinnerParser.Declare_statementContext context)
        {
            // Declare statements do not participate in code generation
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

        // TODO: figure out a better way to do operators
        internal static readonly Dictionary<int, Operator> TokensToOperators = new Dictionary<int, Operator>
        {
            // operators for the standard expressions
            { YarnSpinnerLexer.OPERATOR_LOGICAL_LESS_THAN_EQUALS, Operator.LessThanOrEqualTo },
            { YarnSpinnerLexer.OPERATOR_LOGICAL_GREATER_THAN_EQUALS, Operator.GreaterThanOrEqualTo },
            { YarnSpinnerLexer.OPERATOR_LOGICAL_LESS, Operator.LessThan },
            { YarnSpinnerLexer.OPERATOR_LOGICAL_GREATER, Operator.GreaterThan },
            { YarnSpinnerLexer.OPERATOR_LOGICAL_EQUALS, Operator.EqualTo },
            { YarnSpinnerLexer.OPERATOR_LOGICAL_NOT_EQUALS, Operator.NotEqualTo },
            { YarnSpinnerLexer.OPERATOR_LOGICAL_AND, Operator.And },
            { YarnSpinnerLexer.OPERATOR_LOGICAL_OR, Operator.Or },
            { YarnSpinnerLexer.OPERATOR_LOGICAL_XOR, Operator.Xor },
            { YarnSpinnerLexer.OPERATOR_LOGICAL_NOT, Operator.Not },
            { YarnSpinnerLexer.OPERATOR_MATHS_ADDITION, Operator.Add },
            { YarnSpinnerLexer.OPERATOR_MATHS_SUBTRACTION, Operator.Minus },
            { YarnSpinnerLexer.OPERATOR_MATHS_MULTIPLICATION, Operator.Multiply },
            { YarnSpinnerLexer.OPERATOR_MATHS_DIVISION, Operator.Divide },
            { YarnSpinnerLexer.OPERATOR_MATHS_MODULUS, Operator.Modulo },
        };

        //        // operators for the standard expressions
        //{ YarnSpinnerLexer.OPERATOR_LOGICAL_LESS_THAN_EQUALS, "<=" },
        //{ YarnSpinnerLexer.OPERATOR_LOGICAL_GREATER_THAN_EQUALS, ">=" },
        //{ YarnSpinnerLexer.OPERATOR_LOGICAL_LESS, "<" },
        //{ YarnSpinnerLexer.OPERATOR_LOGICAL_GREATER, ">" },
        //{ YarnSpinnerLexer.OPERATOR_LOGICAL_EQUALS, "==" },
        //{ YarnSpinnerLexer.OPERATOR_LOGICAL_NOT_EQUALS, "!=" },
        //{ YarnSpinnerLexer.OPERATOR_LOGICAL_AND, "&&" },
        //{ YarnSpinnerLexer.OPERATOR_LOGICAL_OR, "||" },
        //{ YarnSpinnerLexer.OPERATOR_LOGICAL_XOR, "unsupported" },
        //{ YarnSpinnerLexer.OPERATOR_LOGICAL_NOT, "!" },
        //{ YarnSpinnerLexer.OPERATOR_MATHS_ADDITION, "+" },
        //{ YarnSpinnerLexer.OPERATOR_MATHS_SUBTRACTION, "-" },
        //{ YarnSpinnerLexer.OPERATOR_MATHS_MULTIPLICATION, "*" },
        //{ YarnSpinnerLexer.OPERATOR_MATHS_DIVISION, "/" },
        //{ YarnSpinnerLexer.OPERATOR_MATHS_MODULUS, "%" },

        /// <summary>
        /// Lists the available operators that can be used with Yarn values.
        /// </summary>
        internal enum Operator
        {
            /// <summary>A unary operator that returns its input.</summary>
            None,

            /// <summary>A binary operator that represents equality.</summary>
            EqualTo,

            /// <summary>A binary operator that represents a value being
            /// greater than another.</summary>
            GreaterThan,

            /// <summary>A binary operator that represents a value being
            /// greater than or equal to another.</summary>
            GreaterThanOrEqualTo,

            /// <summary>A binary operator that represents a value being less
            /// than another.</summary>
            LessThan,

            /// <summary>A binary operator that represents a value being less
            /// than or equal to another.</summary>
            LessThanOrEqualTo,

            /// <summary>A binary operator that represents
            /// inequality.</summary>
            NotEqualTo,

            /// <summary>A binary operator that represents a logical
            /// or.</summary>
            Or,

            /// <summary>A binary operator that represents a logical
            /// and.</summary>
            And,

            /// <summary>A binary operator that represents a logical exclusive
            /// or.</summary>
            Xor,

            /// <summary>A binary operator that represents a logical
            /// not.</summary>
            Not,

            /// <summary>A unary operator that represents negation.</summary>
            UnaryMinus,

            /// <summary>A binary operator that represents addition.</summary>
            Add,

            /// <summary>A binary operator that represents
            /// subtraction.</summary>
            Minus,

            /// <summary>A binary operator that represents
            /// multiplication.</summary>
            Multiply,

            /// <summary>A binary operator that represents division.</summary>
            Divide,

            /// <summary>A binary operator that represents the remainder
            /// operation.</summary>
            Modulo,
        }

    }

}
