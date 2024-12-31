
ifndef YSBUILD
	export YSBUILD = threadbare_build
endif

ifndef YSSCRIPTS
	YSSCRIPTS = src/ysScripts
endif



ifndef YSTOOL
	# export YSTOOL = dotnet run -ys $(YSSCRIPTS) -cpp $(YSBUILD) -h $(YSBUILD) --project "$(LIBTHREADBARE)/ThreadBareCompiler"
	export YSTOOL = $(LIBTHREADBARE)/publish/ThreadBareCompiler -ys $(YSSCRIPTS) -cpp $(YSBUILD) -h $(YSBUILD) -include "script.h"
endif

ifndef YSINCLUDE
	export YSINCLUDE = $(YSBUILD) $(LIBTHREADBARE)/ThreadBareCPPLib/include
endif

ifndef YSSRC
	export YSSRC = $(YSBUILD) $(LIBTHREADBARE)/ThreadBareCPPLib/src
endif
