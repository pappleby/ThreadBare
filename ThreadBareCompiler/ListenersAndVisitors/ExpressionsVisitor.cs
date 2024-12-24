using System.Globalization;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using global::Yarn.Compiler;
using static Yarn.Compiler.YarnSpinnerParser;

namespace ThreadBare
{
    internal class ExpressionsVisitor : YarnSpinnerParserBaseVisitor<int>
    {
        protected Compiler compiler;

        internal string? trackingEnabled = null;
        public Stack<string> parameters = new Stack<string>();

        public ExpressionsVisitor(Compiler compiler, string trackingEnabled)
        {
            this.compiler = compiler;
            this.trackingEnabled = trackingEnabled;
        }
        public void AddParameter(string p)
        {
            parameters.Push(p);
        }
        public string FlushParamaters()
        {
            var debugString = string.Join(" ", parameters);
            parameters.Clear();
            return debugString;
        }

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
            var ps = this.parameters;
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
        public override int VisitTypeMemberReference([NotNull] TypeMemberReferenceContext context)
        {
            var typeName = context.typeName?.Text ?? "TODOLOOKUPENUMNAME";
            // if typeName is null, then we need to lookup enum case names and find the enum name
            // Think this only works for case names that are unique for all enums (unless there's some fancy typechecking way to do this)
            var memberName = context.memberName.Text;
            // Maybe could figure out how to replace with different access characters (ie :: instead of . )
            // That could enable enum raw mode, and/or make enum definitions less painful
            // TODO move enum visiting out of GBA visitor, so that we can handle ".UniqueEnumValue" syntax
            this.AddParameter($"{typeName}.{memberName}");
            return 0;
        }
        // variable
        public override int VisitExpValue(YarnSpinnerParser.ExpValueContext context)
        {
            // Does this get used for anything other than enums?
            return this.Visit(context.value());
        }
        public override int VisitValueVar(YarnSpinnerParser.ValueVarContext context)
        {
            return this.Visit(context.variable());
        }

        public override int VisitValueNumber(YarnSpinnerParser.ValueNumberContext context)
        {
            float number = float.Parse(context.NUMBER().GetText(), CultureInfo.InvariantCulture);
            var numberString = number.ToString();
            if (numberString.Contains('.'))
            {
                this.parameters.Push($"bn::fixed({numberString})");
            }
            else
            {
                this.parameters.Push(numberString);
            }
            return 0;
        }

        public override int VisitValueTrue(YarnSpinnerParser.ValueTrueContext context)
        {
            this.parameters.Push("true");
            return 0;
        }

        public override int VisitValueFalse(YarnSpinnerParser.ValueFalseContext context)
        {
            this.parameters.Push("false");
            return 0;
        }
        public override int VisitVariable(YarnSpinnerParser.VariableContext context)
        {
            string variableName = context.VAR_ID().GetText().Replace("$", "runner.variables.");
            this.parameters.Push(variableName);

            return 0;
        }

        public override int VisitValueString(YarnSpinnerParser.ValueStringContext context)
        {
            string stringVal = context.STRING().GetText();
            this.parameters.Push(stringVal);
            return 0;
        }
        // all we need do is visit the function itself, it will handle
        // everything
        public override int VisitValueFunc(YarnSpinnerParser.ValueFuncContext context)
        {
            this.Visit(context.function_call());
            return 0;
        }
        #endregion

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

        private void GenerateCodeForFunctionCall(string functionName, YarnSpinnerParser.Function_callContext functionContext, YarnSpinnerParser.ExpressionContext[] parameters)
        {
            var compiledParameters = new List<string>();
            var ps = this.parameters;
            // generate the instructions for all of the parameters
            foreach (var parameter in parameters)
            {
                this.Visit(parameter);
                var p = ps?.Pop();
                if (!string.IsNullOrEmpty(p)) { compiledParameters.Add(p); }
            }
            var trimmedFirstParam = compiledParameters.FirstOrDefault("").Trim().Trim('"');
            if (functionName == "visited")
            {
                compiledParameters[0] = $"VisitedNodeName::{trimmedFirstParam}";
                functionName = "runner.VisitedNode";

            }
            else if (functionName == "visited_count")
            {
                compiledParameters[0] = $"VisitCountedNodeName::{trimmedFirstParam}";
                functionName = "runner.VisitedCountNode";
            }
            else
            {
                functionName = "runner.variables." + functionName;
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

        #endregion

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

        /// <summary>
        /// Gets the total number of boolean operations - ands, ors, nots, and
        /// xors - present in an expression and its sub-expressions.
        /// </summary>
        /// <param name="context">An expression.</param>
        /// <returns>The total number of boolean operations in the
        /// expression.</returns>
        public static int GetBooleanOperatorCountInExpression(ParserRuleContext context)
        {
            var subtreeCount = 0;

            if (context is ExpAndOrXorContext || context is ExpNotContext)
            {
                // This expression is a boolean expression.
                subtreeCount += 1;
            }

            foreach (var child in context.children)
            {
                if (child is ParserRuleContext childContext)
                {
                    subtreeCount += GetBooleanOperatorCountInExpression(childContext);
                }
            }

            return subtreeCount;
        }

    }
}
