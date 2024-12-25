#include "threadbare.h"
#include <bn_array.h>
#include <bn_assert.h>
#include <bn_string.h>
#include <bn_sstream.h>
#include <bn_random.h>
#include <bn_fixed.h>

static auto bnrandom = bn::random();
constexpr bn::fixed fps(59.73);

namespace ThreadBare {
using os = bn::ostringstream &;

void TBScriptRunner::Stop() {
    this->nodeStates.clear();
    this->state = Off;
}

void TBScriptRunner::EndNode() {
    this->nodeStates.pop_back();
    if(this->nodeStates.empty()){
        this->state = Off;
    } else {
        this->state = Working;
    }
}

void TBScriptRunner::SafeJump(void (*node)(TBScriptRunner&, NodeState&)) {
    this->nodeStates.pop_back();
    this->nodeStates.emplace_back(NodeState(node));
    this->state = Working; 
}

void TBScriptRunner::Jump(void (*node)(TBScriptRunner&, NodeState&)) {
    this->nodeStates.clear();
    this->nodeStates.emplace_back(NodeState(node));
    this->state = Working; 
}

void TBScriptRunner::Detour(void (*node)(TBScriptRunner&, NodeState&)) {
    this->nodeStates.emplace_back(NodeState(node));
    this->state = Working; 
}

void TBScriptRunner::StartTimer(int seconds, int toNextStep) {
    this->StartTimer(bn::fixed(seconds), toNextStep);
};
void TBScriptRunner::StartTimer(bn::fixed seconds, int toNextStep) {
    this->waitTimer = (fps * seconds).ceil_integer();
    this->state = Timer;
    if(toNextStep != 0){
        this->nodeStates.back().nextStep = toNextStep;
    }
};
TBState TBScriptRunner::Execute() {
    bnrandom.update(); 
    while(!this->nodeStates.empty()) {
        this->nodeStates.back().callNodeFn(*this);
        switch(this->state){
            case TBState::Working:
                continue;
            default:
                return this->state;
        }
    }
    return Off;
}
void TBScriptRunner::FinishLine(int toNextStep) {
    bnrandom.update();
    this->state = Line;
    this->nodeStates.back().nextStep = toNextStep;
}
void TBScriptRunner::ReturnAndGoto(int toNextStep) {
    this->state = Working;
    this->nodeStates.back().nextStep = toNextStep;
}
void TBScriptRunner::SetNoValidOption(int toNextStep) {
    this->nodeStates.back().nextStep = toNextStep;
}
void TBScriptRunner::WaitTick()
{
    bnrandom.update();
    if(this->waitTimer <= 0) {
        this->state = Working;
        this->waitTimer = 0;
    } else {
        this->waitTimer--;
    }
}
void TBScriptRunner::ChooseOption(int i)
{
    BN_ASSERT(this->options.size() > i, "Invalid Option Choice: ", i, " max option: ", (this->options.size() - 1));
    this->nodeStates.back().nextStep = this->options[i].nextStep;
    this->state = Working;
}
bool TBScriptRunner::VisitedNode(VisitedNodeName key) {
    return this->visitedNodes.test((int) key);
};

bool TBScriptRunner::Once(OnceKey key) {
    return this->onceTest.test((int) key);
};
void TBScriptRunner::SetOnce(OnceKey key) {
    return this->onceTest.set((int) key, true);
};

int TBScriptRunner::VisitedCountNode(VisitCountedNodeName key) {
    return this->visitCountNodes[(int) key];
};

void TBScriptRunner::SetVisitedState(VisitedNodeName key) {
    this->visitedNodes.set((int)key, true);
};

void TBScriptRunner::IncrementVisitCount(VisitCountedNodeName key) {
    this->visitCountNodes[(int) key] += 1;
};


auto dice(int sides) -> int {
    return bnrandom.get_unbiased_int(sides + 1);
}
auto random() -> bn::fixed {
    return bnrandom.get_unbiased_fixed(0, 1);
}

}