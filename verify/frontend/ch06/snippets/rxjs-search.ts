import { Subject, debounceTime, distinctUntilChanged, from, switchMap } from "rxjs";

const searchTerms = new Subject<string>();     // the input's (input) handler calls searchTerms.next(text)

searchTerms.pipe(
  debounceTime(300),                            // wait for a 300 ms pause in typing
  distinctUntilChanged(),                       // skip a term equal to the previous one
  switchMap((term) => from(searchApi(term))),   // a new term drops the previous request
).subscribe((results) => render(results));

// Supplied by the test: a controllable fake API and a recorder.
type Product = { id: number; name: string };
declare function searchApi(term: string): Promise<Product[]>;
declare function render(results: Product[]): void;

export { searchTerms };
