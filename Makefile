all: wax

_data/map.json: _build-data/_data/map.json
	cp -f _build-data/_data/map.json _data/map.json

_data/raw_images/map: _build-data/_data/raw_images/map
	rm -rf _data/raw_images/map
	cp -rf _build-data/_data/raw_images/map _data/raw_images/map

wax: _data/map.json _data/raw_images/map
	bundle exec rake wax:derivatives:simple map
	bundle exec rake wax:pages map
	bundle exec rake wax:search main
	bundle exec jekyll build

clean:
	bundle exec rake wax:clobber map
	rm -rf _data/map.json _data/raw_images/map