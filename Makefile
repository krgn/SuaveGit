all:
	@./build.sh

test:
	@./build.sh Test

paket.restore:
	@mono .paket/paket.exe restore

paket.install:
	@mono .paket/paket.exe install
