using Yarn.Compiler;

namespace ThreadBare
{

    internal class VariableDependenciesVisitor : YarnSpinnerParserBaseVisitor<int>
    {
        public HashSet<String> dependencies = new HashSet<string>();
        public override int VisitVariable(YarnSpinnerParser.VariableContext context)
        {
            // found a dependency!
            var test = context.GetText();
            dependencies.Add(test);
            return base.VisitVariable(context);
        }
    }
}
