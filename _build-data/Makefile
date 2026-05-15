_data/smr.csv: smr-map-collection archive.zip
	mkdir -p _data
	./smr-map-collection --outPath _data read-zip archive.zip

smr-map-collection:
	dotnet publish ../smr-map-collection.fsproj --output .

archive.zip:
	curl --output archive.zip --location https://archive.org/compress/sid-meiers-railroads-custom-maps-collection

clean:
	rm -rf smr-map-collection smr-map-collection.pdb _data

clean-all: clean
	rm -f archive.zip