#ifndef THREADBARE_H
#define THREADBARE_H
#include <bn_array.h>
#include <bn_string.h>
#include <bn_sstream.h>
#include <bn_vector.h>
#include <bn_bitset.h>
#include "tb_variables.h"

namespace ThreadBare {
using os = bn::ostringstream &;

enum TBState : int {Line, Options, Timer, Paused, Off, Working};
enum VisitedNodeKey: int{};
class TBScriptRunner;

union AttributeParameterValue {int i; const char* str; bool b;
    AttributeParameterValue(int val) : i(val) {}
    AttributeParameterValue(const char* s) : str(s) {}
    AttributeParameterValue(bool val) : b(val) {}
};

class Markup {
    public:
        bn::vector<LineTag, MAX_TAGS_COUNT> tags;
        bn::vector<int, MAX_TAG_PARAMS_COUNT> tagParams;
        bn::vector<Attribute, MAX_ATTRIBUTES_COUNT> attributes;
        bn::vector<int, MAX_ATTRIBUTES_COUNT> attributePositions;
        bn::vector<AttributeParameterValue, MAX_ATTRIBUTE_PARAMS_COUNT> attributeParams;
        void clear() {
            this->tags.clear();
            this->tagParams.clear();
            this->attributes.clear();
            this->attributePositions.clear();
            this->attributeParams.clear();
        };
};

template<int MaxSize>
class TextBuffer {
    protected: 
        bn::string<MaxSize> text;
        bn::ostringstream textStream;
    public:
        Markup markup;
        bool condition = true;
        
        TextBuffer(): textStream(text){};
        
        void StartNewLine() {
            text.clear();
            markup.clear(); // Can probably optimize away redundant clears if needed
            condition = true;
        }
        auto GetStringView() -> bn::string_view {
            return bn::string_view(this->text);
        }
        template <typename T>
        TextBuffer& operator<<(const T& x) {
            textStream << x;
            return *this;
        }
        TextBuffer& operator<<(bn::ostringstream& (*manip)(bn::ostringstream&)) {
            textStream << manip;
            return *this;
        }
        int length() {
            return text.length();
        }
};

template<int MaxSize>
class Option : public TextBuffer<MaxSize> {
    public:
        int nextStep;
        int trueConditionCount = 0;
        int conditionCount = 0;
        int complexity = 0;
        Option(int setNextStep): TextBuffer<MaxSize>() {
            this->nextStep = setNextStep;
        };
        Option(int setNextStep, bn::string_view text): TextBuffer<MaxSize>() {
            this->nextStep = setNextStep;
            this->textStream << text;
        };
};

class NodeState {
        NodeState() = delete;
    public:
        void (*nodeFn)(TBScriptRunner&, NodeState&);
        int nextStep = 0;
        bn::vector<NodeTag, MAX_NODE_TAGS_COUNT> tags;
        bn::vector<int, MAX_NODE_TAG_PARAMS_COUNT> tagParams;
        NodeState(void (*node)(TBScriptRunner&, NodeState&)){
            this->nodeFn = node;
        }
        void callNodeFn(TBScriptRunner& runner){
            BN_ASSERT(this->nodeFn != nullptr, "nodeStateFn is null");
            this->nodeFn(runner, *this);
        }
};
class TBScriptStorage {
    u_int8_t onceStorage[ONCE_VARIABLE_COUNT / 8] = {0};
    u_int8_t visitedStorage[VISITED_NODE_COUNT / 8] = {0};
    bn::array<int, VISIT_COUNT_NODE_COUNT> visitCountNodes = {0};
    friend class TBScriptRunner;
};

class TBScriptRunner {
    public:
        TBScriptStorage storage;
        bn::bitset_ref visitedNodes;
        bn::bitset_ref onceTest;
        void SafeJump(TBNode node);
        void Jump(TBNode node);
        void Detour(TBNode node);
        TBState state = TBState::Off;
        int waitTimer = 0;
        TextBuffer<LINE_BUFFER_SIZE> currentLine;
        bn::vector<Option<OPTION_BUFFER_SIZE>, MAX_OPTIONS_COUNT> options;
        TBFunctions functions;
        TBVariables variables;
        bn::vector<NodeState, 4> nodeStates;
        TBScriptRunner() : storage(), visitedNodes(storage.visitedStorage), onceTest(storage.onceStorage) {};
        void StartTimer(int seconds, int toNextStep = 0 );
        void StartTimer(bn::fixed seconds, int toNextStep = 0);
        void RunScript(Node node);
        void EndNode();
        void Stop();
        void FinishLine(int toNextStep);
        void ReturnAndGoto(int toNextStep);    
        void SetNoValidOption(int toNextStep);
        void WaitTick();
        void ChooseOption(int i);
        bool VisitedNode(VisitedNodeName key);
        int  VisitedCountNode(VisitCountedNodeName key);
        void SetVisitedState(VisitedNodeName key);
        bool Once(OnceKey key);
        void SetOnce(OnceKey key);
        void IncrementVisitCount(VisitCountedNodeName key);

        TBState Execute();
        
};


}
#endif