using Yarn;
using Yarn.Compiler;

namespace ThreadBare
{
    internal class GbaListener : YarnSpinnerParserBaseListener
    {
        public required Compiler compiler;
        protected ExpressionsVisitor expressionVisitor;

        public GbaListener()
        {
            this.expressionVisitor = new ExpressionsVisitor(compiler!, "false");
        }

        /// <summary>
        /// we have found a new node set up the currentNode var ready to
        /// hold it and otherwise continue
        /// </summary>
        /// <inheritdoc/>
        public override void EnterNode(YarnSpinnerParser.NodeContext context)
        {
            var headerContext = context.title_header().First();
            var nodeTitle = headerContext.title.Text;
            if (!compiler.Nodes.TryGetValue(nodeTitle, out var currentNode))
            {
                var sanitizedFileName = compiler.CurrentFileName.Replace(".yarn", "");
                nodeTitle = $"_{nodeTitle}_{sanitizedFileName}_{context.Start.StartIndex}";
                currentNode = compiler.Nodes[nodeTitle];
            }
            compiler.CurrentNode = currentNode;
        }
        /// <summary>
        /// have left the current node store it into the program wipe the
        /// var and make it ready to go again
        /// </summary>
        /// <inheritdoc />
        public override void ExitNode(YarnSpinnerParser.NodeContext context)
        {
            compiler.CurrentNode = null;
        }
        public override void ExitHeader_when_expression(YarnSpinnerParser.Header_when_expressionContext context)
        {
            var cn = compiler.CurrentNode!;
            cn.isInNodeGroup = true;
            if (context.always != null)
            {
                //cn.conditions.Add("true");
                return;
            }

            if (context.once != null)
            {
                cn.isOnce = true;
                cn.complexity += 1;
                return;
            }
            // Should probably evaluate the complexity here and store it on the node?
            var expression = context.expression();
            this.expressionVisitor.Visit(expression);
            var complexity = ExpressionsVisitor.GetBooleanOperatorCountInExpression(expression);
            cn.complexity += complexity;
            cn.conditions.Add(this.expressionVisitor.FlushParamaters());
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

            if (headerKey.Equals("tags", StringComparison.InvariantCulture))
            {
                // Split the list of tags by spaces, and use that
                var tags = headerValue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var tag in tags)
                {
                    compiler.CurrentNode?.AddTag(tag);
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
            var gbaVisitor = new GbaVisitor(compiler, "false");
            foreach (var statement in context.statement())
            {
                gbaVisitor.Visit(statement);
            }
        }
    }
}
