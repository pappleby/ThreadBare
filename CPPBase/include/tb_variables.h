#ifndef TBVARIABLES_H
#define TBVARIABLES_H

#include <bn_string.h>

namespace ThreadBare {
    // Might be able to get rough estimates for these
    // not sure how to handle expressions though (esp for ones that return strings)
    constexpr static int LINE_BUFFER_SIZE = 300;
    constexpr static int OPTION_BUFFER_SIZE = 100;

    class TBFunctions {

    };

    class TBVariables {
        public:
            bn::string<12> name = "doug";
            int test = 3;
            int foo = 4;
            int bar = 5;
            int gold = 0;
            int loop = 0;
            bool autocontinue = false;
    };
}
#endif