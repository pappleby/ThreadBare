using Antlr4.Runtime.Misc;
using Yarn;
using Yarn.Compiler;

namespace ThreadBare
{
    /// <summary>
    /// Responsible for getting the initial node tree (along with corresponding filenames, and node group internal names),
    /// visited node names, and enums, storing them in the compiler
    /// (maybe also handle variable names / default values at some point?)
    /// </summary>
    internal class DefinitionsListener : YarnSpinnerParserBaseListener
    {
        public required Compiler compiler;
        public override void EnterNode(YarnSpinnerParser.NodeContext context)
        {
            var newNode = new Node { compiler = compiler };
            newNode.nodeIntervalStart = context.Start.StartIndex;
            newNode.filename = context.SourceFileName ?? "";

            compiler.CurrentNode = newNode;
        }
        /// <summary>
        /// have left the current node store it into the program wipe the
        /// var and make it ready to go again
        /// </summary>
        /// <inheritdoc />
        public override void ExitNode(YarnSpinnerParser.NodeContext context)
        {
            var cn = compiler.CurrentNode!;
            var nodeTitle = cn.Name;

            if (cn.isInNodeGroup)
            {
                compiler.NodeNames.Add(nodeTitle);
                compiler.NodeGroupNames.Add(nodeTitle);
                var sanitizedFileName = cn.filename.Replace(".yarn", "");
                nodeTitle = $"_{nodeTitle}_{sanitizedFileName}_{cn.nodeIntervalStart}";
                if (cn.isOnce)
                {
                    compiler.VisitedNodeNames.Add(nodeTitle);
                }
            }
            cn.Name = nodeTitle;

            var nodeKey = compiler.CurrentNode!.Name;
            compiler.Nodes.Add(nodeKey, compiler.CurrentNode);
            compiler.NodeNames.Add(nodeKey);
            compiler.CurrentNode = null;
        }
        public override void ExitTitle_header([NotNull] YarnSpinnerParser.Title_headerContext context)
        {
            var cn = compiler.CurrentNode!;
            var nodeTitle = context.title.Text;
            cn.Name = nodeTitle;
            cn.OriginalName = nodeTitle;
            compiler.CurrentNode!.Name = nodeTitle;
        }

        /// <summary> 
        /// have finished with the header so about to enter the node body
        /// and all its statements do the initial setup required before
        /// compiling that body statements eg emit a new startlabel
        /// </summary>
        /// <inheritdoc />
        public override void ExitHeader(YarnSpinnerParser.HeaderContext context)
        {
            var cn = compiler.CurrentNode!;
            var headerKey = context.header_key.Text;

            // Use the header value if provided, else fall back to the
            // empty string. compiler means that a header like "foo: \n" will
            // be stored as 'foo', '', consistent with how it was typed.
            // That is, it's not null, because a header was provided, but
            // it was written as an empty line.
            var headerValue = context.header_value?.Text ?? String.Empty;

            if (headerKey.Equals("tags", StringComparison.InvariantCulture))
            {
                // Split the list of tags by spaces, and use that
                var tags = headerValue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var tag in tags)
                {
                    cn.AddTag(tag);
                }

                //if (compiler.CurrentNode.Tags.Contains("rawText"))
                //{
                //    // compiler is a raw text node. Flag it as such for future
                //    // compilation.
                //    compiler.RawTextNode = true;
                //}
            }
            var header = new Header();
            header.Key = headerKey;
            header.Value = headerValue;
        }
        public override void ExitHeader_when_expression(YarnSpinnerParser.Header_when_expressionContext context)
        {
            var cn = compiler.CurrentNode!;
            cn.isInNodeGroup = true;
            if (context.once != null)
            {
                cn.isOnce = true;
            }

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
            var definitionsVisitor = new DefinitionsVisitor(compiler, "false");
            foreach (var statement in context.statement())
            {
                definitionsVisitor.Visit(statement);
            }
        }

        public override void ExitDeclare_statement([NotNull] YarnSpinnerParser.Declare_statementContext context)
        {
            var name = context.variable().GetText();
            var valueContext = context.expression();
            var isLiteral = valueContext.ChildCount == 1 && valueContext.children[0] is YarnSpinnerParser.ILiteralContext;
            if (isLiteral)
            {
                this.compiler.Variables[name] = valueContext.GetText();
            }
            else
            {
                this.compiler.Variables.Remove(name);
                var t = new VariableDependenciesVisitor();

                t.Visit(valueContext);
                var dependencies = t.dependencies;
                if (dependencies.Any())
                {
                    this.compiler.UnresolvedSmartVariables.Add(name, new SmartVariable { Name = name, Dependencies = dependencies, Expression = valueContext });
                }
                else
                {
                    this.compiler.ResolvedSmartVariables.Add(name, valueContext.GetText());
                }
            }

        }

    }
}
