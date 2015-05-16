﻿module AsyncSeqTests

open NUnit.Framework
open FSharp.Control
open System

module AsyncOps = 
    let unit = async.Return()
    let never = Async.Sleep(-1)

/// Determines equality of two async sequences by convering them to lists, ignoring side-effects.
let EQ (a:AsyncSeq<'a>) (b:AsyncSeq<'a>) = 
  let exp = a |> AsyncSeq.toList |> Async.RunSynchronously
  let act = b |> AsyncSeq.toList |> Async.RunSynchronously  
  if (exp = act) then true
  else
    printfn "expected=%A" exp
    printfn "actual=%A" act
    false


[<Test>]
let ``AsyncSeq.toArray``() =  
  let ls = [1;2;3]
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
  }
  let a = s |> AsyncSeq.toArray |> Async.RunSynchronously |> Array.toList
  Assert.True(([1;2;3] = a))


[<Test>]
let ``AsyncSeq.toList``() =  
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
  }
  let a = s |> AsyncSeq.toList |> Async.RunSynchronously
  Assert.True(([1;2;3] = a))


[<Test>]
let ``AsyncSeq.concatSeq``() =  
  let ls = [ [1;2] ; [3;4] ]
  let actual = AsyncSeq.ofSeq ls |> AsyncSeq.concatSeq    
  let expected = ls |> List.concat |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.unfoldAsync``() =  
  let gen s =
    if s < 3 then (s,s + 1) |> Some
    else None
  let expected = Seq.unfold gen 0 |> AsyncSeq.ofSeq
  let actual = AsyncSeq.unfoldAsync (gen >> async.Return) 0
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.interleave``() =  
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq [1;2;3]
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toList |> Async.RunSynchronously
  Assert.True([Choice1Of2 "a" ; Choice2Of2 1 ; Choice1Of2 "b" ; Choice2Of2 2 ; Choice1Of2 "c" ; Choice2Of2 3] = merged)


[<Test>]
let ``AsyncSeq.bufferByCount``() =
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
    yield 4
    yield 5
  }
  let s' = s |> AsyncSeq.bufferByCount 2 |> AsyncSeq.toList |> Async.RunSynchronously
  Assert.True(([[|1;2|];[|3;4|];[|5|]] = s'))

[<Test>]
let ``AsyncSeq.bufferByCountAndTime``() =      
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
    do! Async.Sleep 500
    yield 4
    yield 5
  }
  let s' = AsyncSeq.bufferByCountAndTime 2 10 s |> AsyncSeq.toList |> Async.RunSynchronously
  Assert.True(([ [|1;2|] ; [|3|] ; [|4;5|] ] = s'))

//[<Test>]
//let ``AsyncSeq.bufferBy``() =      
//  let s = asyncSeq {
//    yield 1
//    yield 2
//    do! Async.Sleep 500
//    yield 3    
//    yield 4
//    do! Async.Sleep 500
//    yield 5
//  }
//  let s' = AsyncSeq.bufferBy (Async.Sleep 10) s |> AsyncSeq.toList |> Async.RunSynchronously
//  Assert.True(([ [|1;2|] ; [|3;4|] ; [|5|] ] = s'))

[<Test>]
let ``AsyncSeq.bufferByTime``() =      
  let s = asyncSeq {
    yield 1
    yield 2
    do! Async.Sleep 500
    yield 3    
    yield 4
    do! Async.Sleep 500
    yield 5
  }
  let s' = AsyncSeq.bufferByTime 10 s |> AsyncSeq.toList |> Async.RunSynchronously
  Assert.True(([ [|1;2|] ; [|3;4|] ; [|5|] ] = s'))

[<Test>]
let ``AsyncSeq.zip``() =  
  let la = [1;2;3;4;5]
  let lb = [1;2;3;4;5]
  let a = la |> AsyncSeq.ofSeq
  let b = lb |> AsyncSeq.ofSeq
  let actual = AsyncSeq.zip a b
  let expected = List.zip la lb |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.zipWithAsync``() =  
  let la = [1;2;3;4;5]
  let lb = [1;2;3;4;5]
  let a = la |> AsyncSeq.ofSeq
  let b = lb |> AsyncSeq.ofSeq
  let actual = AsyncSeq.zipWithAsync (fun a b -> a + b |> async.Return) a b
  let expected = List.zip la lb |> List.map ((<||) (+)) |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.skipWhileAsync``() =  
  let ls = [1;2;3;4;5]
  let p i = i <= 2
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.skipWhileAsync (p >> async.Return)
  let expected = ls |> Seq.skipWhile p |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.takeWhileAsync``() =  
  let ls = [1;2;3;4;5]
  let p i = i < 4
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.takeWhileAsync (p >> async.Return)
  let expected = ls |> Seq.takeWhile p |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.take``() =  
  let ls = [1;2;3;4;5]
  let c = 3
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.take c
  let expected = ls |> Seq.take c |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.skip``() =
  let ls = [1;2;3;4;5]
  let c = 3
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.skip c
  let expected = ls |> Seq.skip c |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.threadStateAsync``() =
  let ls = [1;2;3;4;5]
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.threadStateAsync (fun i a -> async.Return(i + a, i + 1)) 0
  let expected = [1;3;5;7;9] |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.scanAsync``() =
  let ls = [1;2;3;4;5]
  let f i a = i + a
  let z = 0
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.scanAsync (fun i a -> f i a |> async.Return) z
  let expected = ls |> List.scan f z |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.scanAsync on empty should emit initial state``() =
  let ls = List.empty
  let f i a = i + a
  let z = 0
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.scanAsync (fun i a -> f i a |> async.Return) z
  let expected = ls |> List.scan f z |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.foldAsync``() =
  let ls = [1;2;3;4;5]
  let f i a = i + a
  let z = 0
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.foldAsync (fun i a -> f i a |> async.Return) z |> Async.RunSynchronously
  let expected = ls |> Seq.fold f z
  Assert.True((expected = actual))


[<Test>]
let ``AsyncSeq.filterAsync``() =
  let ls = [1;2;3;4;5]
  let p i = i > 3
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.filterAsync (p >> async.Return)
  let expected = ls |> Seq.filter p |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.merge``() =
  let ls1 = [1;2;3;4;5]
  let ls2 = [6;7;8;9;10]
  let actual = AsyncSeq.merge (AsyncSeq.ofSeq ls1) (AsyncSeq.ofSeq ls2) |> AsyncSeq.toList |> Async.RunSynchronously |> Set.ofList
  let expected = ls1 @ ls2 |> Set.ofList
  Assert.True((expected = actual))


[<Test>]
let ``AsyncSeq.merge should be fair``() =  
  let s1 = asyncSeq {
    do! Async.Sleep 10
    yield 1
  }
  let s2 = asyncSeq {
    yield 2
  }
  let actual = AsyncSeq.merge s1 s2
  let expected = [2;1] |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.replicate``() =
  let c = 10
  let x = "hello"
  let actual = AsyncSeq.replicate x |> AsyncSeq.take c
  let expected = List.replicate c x |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.traverseOptionAsync``() =
  let seen = ResizeArray<_>()
  let s = [1;2;3;4;5] |> AsyncSeq.ofSeq
  let f i =
    seen.Add i
    if i < 2 then Some i |> async.Return
    else None |> async.Return
  let r = AsyncSeq.traverseOptionAsync f s |> Async.RunSynchronously
  match r with
  | Some _ -> Assert.Fail()
  | None -> Assert.True(([1;2] = (seen |> List.ofSeq)))

[<Test>]
let ``AsyncSeq.traverseChoiceAsync``() =
  let seen = ResizeArray<_>()
  let s = [1;2;3;4;5] |> AsyncSeq.ofSeq
  let f i =
    seen.Add i
    if i < 2 then Choice1Of2 i |> async.Return
    else Choice2Of2 "oh no" |> async.Return
  let r = AsyncSeq.traverseChoiceAsync f s |> Async.RunSynchronously
  match r with
  | Choice1Of2 _ -> Assert.Fail()
  | Choice2Of2 e -> 
    Assert.AreEqual("oh no", e)
    Assert.True(([1;2] = (seen |> List.ofSeq)))
  

[<Test>]
let ``AsyncSeq.toBlockingSeq does not hung forever and rethrows exception``() =
  let s = asyncSeq {
      yield 1
      failwith "error"
  }
  Assert.Throws<Exception>(fun _ -> s |> AsyncSeq.toBlockingSeq |> Seq.toList |> ignore) |> ignore
  try 
      let _ = s |> AsyncSeq.toBlockingSeq |> Seq.toList 
      ()
  with e -> 
      Assert.AreEqual(e.Message, "error") 


[<Test>]
let ``AsyncSeq.distinctUntilChangedWithAsync``() =  
  let ls = [1;1;2;2;3;4;5;1]
  let s = ls |> AsyncSeq.ofSeq
  let c a b =
    if a = b then true |> async.Return
    else false |> async.Return
  let actual = s |> AsyncSeq.distinctUntilChangedWithAsync c
  let expected = [1;2;3;4;5;1] |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.takeUntil should complete immediately with completed signal``() =  
  let s = asyncSeq {
    do! Async.Sleep 10
    yield 1
    yield 2
  }
  let actual = AsyncSeq.takeUntil AsyncOps.unit s
  Assert.True(EQ AsyncSeq.empty actual)


[<Test>]
let ``AsyncSeq.takeUntil should take entire sequence with never signal``() =
  let expected = [1;2;3;4] |> AsyncSeq.ofSeq
  let actual = expected |> AsyncSeq.takeUntil AsyncOps.never
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.skipUntil should not skip with completed signal``() =
  let expected = [1;2;3;4] |> AsyncSeq.ofSeq
  let actual = expected |> AsyncSeq.skipUntil AsyncOps.unit
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.skipUntil should skip everything with never signal``() =
  let actual = [1;2;3;4] |> AsyncSeq.ofSeq |> AsyncSeq.skipUntil AsyncOps.never
  Assert.True(EQ AsyncSeq.empty actual)

[<Test>]
let ``AsyncSeq.toBlockingSeq should work length 1``() =
  let s = asyncSeq { yield 1 } |> AsyncSeq.toBlockingSeq  |> Seq.toList
  Assert.True((s = [1]))

[<Test>]
let ``AsyncSeq.toBlockingSeq should work length 0``() =
  let s = asyncSeq { () } |> AsyncSeq.toBlockingSeq  |> Seq.toList
  Assert.True((s = []))

[<Test>]
let ``AsyncSeq.toBlockingSeq should work length 2 with sleep``() =
  let s = asyncSeq { yield 1 
                     do! Async.Sleep 10
                     yield 2  } |> AsyncSeq.toBlockingSeq  |> Seq.toList
  Assert.True((s = [1; 2]))

[<Test>]
let ``AsyncSeq.toBlockingSeq should work length 1 with fail``() =
  let s = 
      asyncSeq { yield 1 
                 failwith "fail"  } 
      |> AsyncSeq.toBlockingSeq  
      |> Seq.truncate 1 
      |> Seq.toList
  Assert.True((s = [1]))

[<Test>]
let ``AsyncSeq.toBlockingSeq should work length 0 with fail``() =
  let s = 
      asyncSeq { failwith "fail"  } 
      |> AsyncSeq.toBlockingSeq  
      |> Seq.truncate 0 
      |> Seq.toList
  Assert.True((s = []))

[<Test>]
let ``AsyncSeq.toBlockingSeq should be cancellable``() =
  let cancelCount = ref 0 
  let aseq = 
      asyncSeq { 
          use! a = Async.OnCancel(fun x -> incr cancelCount)
          while true do 
              yield 1 
              do! Async.Sleep 10
    }
    
  let asSeq = aseq |> AsyncSeq.toBlockingSeq
  let enum = asSeq.GetEnumerator()
  Assert.AreEqual(cancelCount.Value, 0)
  let canMoveNext = enum.MoveNext()
  Assert.AreEqual(canMoveNext, true)
  Assert.AreEqual(cancelCount.Value, 0)
  enum.Dispose()
  System.Threading.Thread.Sleep(1000) // wait for task cancellation to be effective
  Assert.AreEqual(cancelCount.Value, 1)

[<Test>]
let ``AsyncSeq.while should allow do at end``() =  
  let s1 = asyncSeq {
    while false do 
        yield 1
        do! Async.Sleep 10
  }
  Assert.True(true)

let observe vs err = 
    let discarded = ref false
    { new IObservable<'U> with 
            member x.Subscribe(observer) = 
                for v in vs do 
                   observer.OnNext v
                if err then 
                   observer.OnError (Failure "fail")
                observer.OnCompleted()
                { new IDisposable with member __.Dispose() = discarded := true }  },
    (fun _ -> discarded.Value)
    
[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (empty)``() =  
  let src, discarded = observe [] false
  Assert.True(src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toList |> Async.RunSynchronously = [])
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (sinngleton)``() =  
  let src, discarded = observe [1] false
  Assert.True(src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toList |> Async.RunSynchronously = [1])
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (ten)``() =  
  let src, discarded = observe [1..10] false
  Assert.True(src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toList |> Async.RunSynchronously = [1..10])
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (empty, fail)``() =  
  let src, discarded = observe [] true
  Assert.True(try (src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toList |> Async.RunSynchronously |> ignore); false with _ -> true)
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (one, fail)``() =  
  let src, discarded = observe [1] true
  Assert.True(try (src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toList |> Async.RunSynchronously |> ignore); false with _ -> true)
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (one, take)``() =  
  let src, discarded = observe [1] true
  Assert.True(src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.take 1 |> AsyncSeq.toList |> Async.RunSynchronously = [1])
  // Take doesn't correctly run finally clauses, see https://github.com/fsprojects/FSharp.Control.AsyncSeq/issues/15
  //Assert.True(discarded())

[<Test>]
let ``AsyncSeq.getIterator should work``() =  
  let s1 = [1..2] |> AsyncSeq.ofSeq
  let i = AsyncSeq.getIterator s1
  match i() |> Async.RunSynchronously with 
  | None -> Assert.Fail("expected Some")
  | Some v -> 
    Assert.AreEqual(v,1)
    match i() |> Async.RunSynchronously with 
    | None -> Assert.Fail("expected Some")
    | Some v -> 
        Assert.AreEqual(v,2)
        match i() |> Async.RunSynchronously with 
        | None -> ()
        | Some _ -> Assert.Fail("expected None")


  
[<Test>]
let ``asyncSeq.For should delay``() =
  let (s:seq<int>) = 
     { new System.Collections.Generic.IEnumerable<int> with 
           member x.GetEnumerator() = failwith "fail" 
       interface System.Collections.IEnumerable with 
           member x.GetEnumerator() = failwith "fail"  }
  Assert.DoesNotThrow(fun _ -> asyncSeq.For(s, (fun v -> AsyncSeq.empty)) |> ignore)
