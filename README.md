Companion website for the community Sid Meier's Railroads! map [collection](https://archive.org/details/sid-meiers-railroads-custom-maps-collection) hosted on the Internet Archive. Map data are extracted from a Zip download of the collection using F# and then rendered to GitHub Pages using the [Wax](https://minicomp.github.io/wax/) template for Jekyll.

### Build

```bash
make -C _build-data/
git checkout wax-website
make
git add _data/ _smr/ img/derivatives/ search/
# commit to the wax-website-deploy branch and run the github action
```
