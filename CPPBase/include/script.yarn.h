#ifndef SCRIPT_YARN_H
#define SCRIPT_YARN_H

namespace ThreadBare {
	class TBScriptRunner;
	class NodeState;
	constexpr static int MAX_OPTIONS_COUNT = 0;
	constexpr static int MAX_TAGS_COUNT = 0;
	constexpr static int MAX_TAG_PARAMS_COUNT = 0;
	constexpr static int MAX_NODE_TAGS_COUNT = 2;
	constexpr static int MAX_NODE_TAG_PARAMS_COUNT = 2;
	constexpr static int MAX_ATTRIBUTES_COUNT = 0;
	constexpr static int MAX_ATTRIBUTE_PARAMS_COUNT = 0;
	constexpr static int VISITED_NODE_COUNT = 8;
	constexpr static int VISIT_COUNT_NODE_COUNT = 1;
	constexpr static int ONCE_VARIABLE_COUNT = 1;

	// nodes names
	enum class Node : int {  };

	// nodes:
	enum class NodeTag : int { };
	enum class VisitedNodeName : int { };
	enum class VisitCountedNodeName : int {  };

	// tags:
	enum class LineTag : int { test, valuetest, doesitwork, hmmm };

	// attributes:
	enum class Attribute : int { character, _character, wave, _wave, att, _att, blahtest, _blahtest };
	// Nodes:
	void DemoStart(TBScriptRunner& runner, NodeState& nodeState);
	void DemoMenu(TBScriptRunner& runner, NodeState& nodeState);
	void Explanations(TBScriptRunner& runner, NodeState& nodeState);
	void DemoDemo(TBScriptRunner& runner, NodeState& nodeState);
	void bgtestAutoRun(TBScriptRunner& runner, NodeState& nodeState);
	void bgtestAutoRunSally(TBScriptRunner& runner, NodeState& nodeState);
	void bgtestAutoRunTrackInterview(TBScriptRunner& runner, NodeState& nodeState);
	void bgtestManualRun(TBScriptRunner& runner, NodeState& nodeState);
	void demo2test(TBScriptRunner& runner, NodeState& nodeState);
}
#endif