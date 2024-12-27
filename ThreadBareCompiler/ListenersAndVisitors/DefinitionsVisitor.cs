using global::Yarn.Compiler;
using static Yarn.Compiler.YarnSpinnerParser;

namespace ThreadBare
{
    internal class DefinitionsVisitor : YarnSpinnerParserBaseVisitor<int>
    {
        protected Compiler compiler;

        internal string? trackingEnabled = null;
        protected ExpressionsVisitor expressionVisitor;
        public DefinitionsVisitor(Compiler compiler, string trackingEnabled)
        {
            this.compiler = compiler;
            this.trackingEnabled = trackingEnabled;
            this.expressionVisitor = new ExpressionsVisitor(compiler, trackingEnabled);
        }

        public override int VisitEnum_statement(Enum_statementContext context)
        {
            var enumName = context.name.Text;
            if (this.compiler.Enums.Any(e => e.Name == enumName))
            {
                return 0;
            }
            var resultEnum = new Enum { Name = enumName };

            foreach (var enumCase in context.enum_case_statement())
            {
                var caseName = enumCase.name.Text;
                var resultCase = new EnumCase { Name = caseName };
                base.VisitEnum_case_statement(enumCase);
                expressionVisitor.Visit(enumCase);
                if (expressionVisitor.parameters.TryPop(out var caseValue))
                {
                    resultCase.Value = caseValue;
                    expressionVisitor.parameters.Clear();
                }
                resultEnum.Cases.Add(resultCase);
            }

            this.compiler.Enums.Add(resultEnum);
            return 0;
        }

        // Used to figure out which nodes need to have visit counts
        public override int VisitFunction_call(Function_callContext context)
        {
            string functionName = context.FUNC_ID().GetText();
            var functionParams = context.expression();

            var compiledParameters = new List<string>();
            foreach (var parameter in functionParams)
            {
                expressionVisitor.Visit(parameter);
                var p = expressionVisitor.parameters?.Pop();
                if (!string.IsNullOrEmpty(p)) { compiledParameters.Add(p); }
            }
            var nodeName = compiledParameters.FirstOrDefault("").Trim().Trim('"');
            if (functionName == "visited")
            {
                this.compiler.VisitedNodeNames.Add(nodeName);
            }
            else if (functionName == "visited_count")
            {
                this.compiler.VisitedCountNodeNames.Add(nodeName);
            }
            // Shouldn't ever be needed, but just in case
            expressionVisitor.parameters?.Clear();
            return 0;
        }
        public override int VisitDeclare_statement(Declare_statementContext context)
        {
            var name = context.variable().GetText();

            var value = context.expression();

            return 0;
        }
        public override int VisitVariable(VariableContext context)
        {
            var test = context.GetText();
            if (!this.compiler.Variables.ContainsKey(test))
            {
                // Keep track of implicit variables
                this.compiler.Variables.Add(test, "");
            }

            return base.VisitVariable(context);
        }
    }
}
