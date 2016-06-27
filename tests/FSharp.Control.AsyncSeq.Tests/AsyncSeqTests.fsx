﻿#r @"../../bin/FSharp.Control.AsyncSeq.dll"
#r @"../../packages/NUnit/lib/nunit.framework.dll"
#nowarn "40"
#time "on"
//#load "AsyncSeqTests.fs"

open System
open FSharp.Control


/// An async sequence transducer.
type AsyncSeqTran<'a, 'b> =
  | Halt
  | Await of ('a option -> Async<AsyncSeqTran<'a, 'b>>)
  | Emit of 'b * Async<AsyncSeqTran<'a, 'b>>

module AsyncSeqTran =
  
  let unfold (f:'s -> Async<('a * 's) option>) (s:'s) : Async<AsyncSeqTran<unit, 'a>> =
    let rec go (s:'s) = async {
      let! res = f s
      match res with
      | Some (a,s') -> return Emit (a, go s')
      | None -> return Halt }
    go s

  let rec compose (g:Async<AsyncSeqTran<'b, 'c>>) (f:Async<AsyncSeqTran<'a, 'b>>) : Async<AsyncSeqTran<'a, 'c>> = async {
    let! g = g
    match g with
    | Halt -> return Halt
    | Emit (c,cont) -> return Emit (c,compose cont f)
    | Await rcvB ->
      let! f = f
      match f with
      | Halt -> return Halt
      | Emit (b,cont) -> return! compose (rcvB (Some b)) cont
      | Await rcvA -> return Await (rcvA >> compose (async.Return (Await rcvB))) }

  let apply (tran:Async<AsyncSeqTran<'a, 'b>>) (source:AsyncSeq<'a>) : AsyncSeq<'b> =
    { new IAsyncEnumerable<'b> with
        member __.GetEnumerator () =
          let en = source.GetEnumerator()
          let tran = ref tran
          { new IAsyncEnumerator<'b> with
              member __.MoveNext () = async {
                let! t = !tran
                let rec go tran = async {
                  match tran with
                  | Halt ->
                    return None
                  | Await cont ->
                    let! next = en.MoveNext()
                    let! tran' = cont next
                    return! go tran'
                  | Emit (a,cont) ->
                    return Some (a,cont) }
                let! next = go t
                match next with
                | None -> 
                  return None
                | Some (a,tran') ->                  
                  tran := tran'
                  return Some a }
              member __.Dispose () = en.Dispose() } }

    
  type AstOp<'a, 'b> =
    | Map of ('a -> Async<'b>)
    | Choose of ('a -> Async<'b option>)


  type Ast<'a, 'b> (op:AstOp<'a, 'b>) =
      
    let mutable op : AstOp<'a, 'b> = op

    // TODO: sync!
    member __.Map (f:'a -> Async<'b>) : unit =
      op <- Map f
     
    member __.Apply (source:AsyncSeq<'a>) : AsyncSeq<'b> =
      { new IAsyncEnumerable<'b> with
          member __.GetEnumerator () =
            let en = source.GetEnumerator ()
            { new IAsyncEnumerator<'b> with
                member __.MoveNext () = async {                    
                  let! next = en.MoveNext ()
                  match next with
                  | Some a ->
                    match op with
                    | Map f -> 
                      let! b = f a
                      return Some b
                    | Choose f ->
                      let! b = f a
                      match b with
                      | Some b -> return Some b
                      | None -> return! __.MoveNext ()
                  | None -> return None }  
                member __.Dispose () =
                  en.Dispose () } }
  


let mapFst f (a,b) = (f a, b)
let mapSnd f (a,b) = (a, f b)

module Async =
  
  let inline map f a = async.Bind (a, f >> async.Return)

module Disposable =
  
  let ofFun (f:unit -> unit) : IDisposable =
    { new IDisposable with member __.Dispose () = f () }

  let empty : IDisposable =
    ofFun ignore


module AsyncSeq =
    
  [<AbstractClass>]
  type AsyncSeqOp<'T> () =
    abstract member ChooseAsync : ('T -> Async<'U option>) -> AsyncSeq<'U>
    abstract member Choose : ('T -> 'U option) -> AsyncSeq<'U>
    abstract member FoldAsync : ('S -> 'T -> Async<'S>) -> 'S -> Async<'S>
    abstract member Fold : ('S -> 'T -> 'S) -> 'S -> Async<'S>
    abstract member MapAsync : ('T -> Async<'U>) -> AsyncSeq<'U>
    abstract member Map : ('T -> 'U) -> AsyncSeq<'U>       
    abstract member Iter : ('T -> unit) -> Async<unit>
    abstract member IterAsync : ('T -> Async<unit>) -> Async<unit>
    abstract member Apply : Async<AsyncSeqTran<'T, 'U>> -> AsyncSeq<'U>
    default x.Apply (t:Async<AsyncSeqTran<'T, 'U>>) : AsyncSeq<'U> =
      failwith ""
    default x.Choose (f:'T -> 'U option) : AsyncSeq<'U> =
      x.ChooseAsync (f >> async.Return)
    default x.Fold (f:'S -> 'T -> 'S) (s:'S) : Async<'S> =
      x.FoldAsync (fun s t -> f s t |> async.Return) s
    default x.MapAsync (f:'T -> Async<'U>) : AsyncSeq<'U> =
      x.ChooseAsync (f >> Async.map Some)
    default x.Map (f:'T -> 'U) : AsyncSeq<'U> =
      x.MapAsync (f >> async.Return)
    default x.IterAsync (f:'T -> Async<unit>) : Async<unit> =
      x.FoldAsync (fun () t -> f t) ()
    default x.Iter (f:'T -> unit) : Async<unit> =
      x.IterAsync (f >> async.Return)

  type ChooseStateAsyncEnumerable<'S, 'T, 'U> (f:'S -> 'T -> Async<('U * 'S) option>, init:'S, source:AsyncSeq<'T>) =
    inherit AsyncSeqOp<'U> ()
    override __.FoldAsync (g:'S2 -> 'U -> Async<'S2>) (init2:'S2) = async {
      //printfn "ChooseAsyncEnumerable.FuseFoldAsync"
      use en = source.GetEnumerator ()
      let rec go s s2 = async {
        let! next = en.MoveNext ()
        match next with
        | Some t ->
          let! res = f s t
          match res with
          | Some (u,s') ->
            let! s2' = g s2 u
            return! go s' s2'
          | None ->
            return s2
        | None ->
          return s2 }
      return! go init init2 }
    override __.ChooseAsync (g:'U -> Async<'V option>) : AsyncSeq<'V> =
      //printfn "ChooseAsyncEnumerable.FuseChooseAsync"
      let f s t = async {
        let! res = f s t
        match res with
        | None -> 
          return None
        | Some (u,s) ->
          let! res' = g u
          match res' with
          | Some v ->
            return Some (v, s)
          | None ->
            return None }
      new ChooseStateAsyncEnumerable<'S, 'T, 'V> (f, init, source) :> _
    override __.Map (g:'U -> 'V) : AsyncSeq<'V> =
      //printfn "ChooseAsyncEnumerable.FuseMap"
      let f s t = async {
        let! res = f s t
        match res with
        | None -> return None
        | Some (u,s) ->
          let v = g u
          return Some (v,s) }          
      new ChooseStateAsyncEnumerable<'S, 'T, 'V> (f, init, source) :> _
    override __.MapAsync (g:'U -> Async<'V>) : AsyncSeq<'V> =
      //printfn "ChooseAsyncEnumerable.FuseMapAsync"
      let f s t = async {
        let! res = f s t
        match res with
        | None -> return None
        | Some (u,s) ->
          let! v = g u
          return Some (v,s) }          
      new ChooseStateAsyncEnumerable<'S, 'T, 'V> (f, init, source) :> _
    interface IAsyncEnumerable<'U> with
      member __.GetEnumerator () =
        let s = ref init
        let st = ref 0
        let mutable en = Unchecked.defaultof<_>
        { new IAsyncEnumerator<'U> with
            member __.MoveNext () : Async<'U option> = async {
              match !st with
              | 0 ->
                en <- source.GetEnumerator()
                st := 1
                return! __.MoveNext ()
              | _ ->
                let! next = en.MoveNext () 
                match next with
                | None -> return None 
                | Some t ->
                  let! res = f !s t
                  match res with
                  | Some (u,s') ->
                    s := s'
                    return Some u
                  | None ->
                    return None }
            member __.Dispose () =
              match !st with
              | 1 ->  en.Dispose()
              | _ -> () }

  type UnfoldAsyncEnumerator<'S, 'T> (f:'S -> Async<('T * 'S) option>, init:'S, disp:IDisposable) =
    inherit AsyncSeqOp<'T> ()
    override __.Apply (t:Async<AsyncSeqTran<'T, 'U>>) : AsyncSeq<'U> =
      AsyncSeqTran.apply 
        (AsyncSeqTran.compose t (AsyncSeqTran.unfold f init))
        (AsyncSeq.empty)
    override x.Iter g = async {
      //printfn "UnfoldAsyncEnumerator.Iter"
      let rec go s = async {
        let! next = f s
        match next with
        | None -> return ()
        | Some (t,s') ->
          do g t
          return! go s' }
      return! go init }
    override x.IterAsync g = async {
      //printfn "UnfoldAsyncEnumerator.IterAsync"
      let rec go s = async {
        let! next = f s
        match next with
        | None -> return ()
        | Some (t,s') ->
          do! g t
          return! go s' }
      return! go init }
    override __.FoldAsync (g:'S2 -> 'T -> Async<'S2>) (init2:'S2) = async {
      //printfn "UnfoldAsyncEnumerator.FoldAsync"
      let rec go s s2 = async {
        let! next = f s
        match next with
        | None -> return s2
        | Some (t,s') ->
          let! s2' = g s2 t
          return! go s' s2' }
      return! go init init2 }
    override __.ChooseAsync (g:'T -> Async<'U option>) : AsyncSeq<'U> =
      //printfn "UnfoldAsyncEnumerator.ChooseAsync"
      let f s = async {
        let! res = f s
        match res with
        | None -> 
          return None
        | Some (t,s) ->
          let! res' = g t
          match res' with
          | Some u ->
            return Some (u, s)
          | None ->
            return None }
      new UnfoldAsyncEnumerator<'S, 'U> (f, init, disp) :> _
    override __.Map (g:'T -> 'U) : AsyncSeq<'U> =
      //printfn "UnfoldAsyncEnumerator.Map"
      let h s = async {
        let! r = f s
        match r with
        | Some (t,s) ->
          let u = g t
          return Some (u,s)
        | None ->
          return None }
      new UnfoldAsyncEnumerator<'S, 'U> (h, init, disp) :> _
    override __.MapAsync (g:'T -> Async<'U>) : AsyncSeq<'U> =
      //printfn "UnfoldAsyncEnumerator.MapAsync"
      let h s = async {
        let! r = f s
        match r with
        | Some (t,s) ->
          let! u = g t
          return Some (u,s)
        | None ->
          return None }
      new UnfoldAsyncEnumerator<'S, 'U> (h, init, disp) :> _
    interface IAsyncEnumerable<'T> with
      member __.GetEnumerator () =
        let s = ref init
        { new IAsyncEnumerator<'T> with
            member __.MoveNext () : Async<'T option> = async {
              let! next = f !s 
              match next with
              | None -> 
                return None 
              | Some (a,s') ->
                s := s'
                return Some a }
            member __.Dispose () =
              disp.Dispose () }
    
  type MapEnumerable<'T, 'U> (f:'T -> 'U, source:AsyncSeq<'T>) =
    inherit AsyncSeqOp<'U> () with
    override __.FoldAsync (g:'S -> 'U -> Async<'S>) (init:'S) = async {
      //printfn "MapEnumerable.FuseFoldAsync"
      use en = source.GetEnumerator ()
      let rec go s = async {
        let! next = en.MoveNext ()
        match next with
        | Some t ->
          let u = f t
          let! s' = g s u
          return! go s'
        | None ->
          return s }
      return! go init }
    override __.ChooseAsync (g:'U -> Async<'V option>) : AsyncSeq<'V> =
      let f () t = async {
        let u = f t
        let! res' = g u
        match res' with
        | Some v ->
          return Some (v, ())
        | None ->
          return None }
      new ChooseStateAsyncEnumerable<unit, 'T, 'V> (f, (), source) :> _
    override __.Map (g:'U -> 'V) : AsyncSeq<'V> = 
      new MapEnumerable<'T, 'V> (f >> g, source) :> _ 
    override __.MapAsync (g:'U -> Async<'V>) : AsyncSeq<'V> =
      new MapAsyncEnumerable<'T, 'V> (f >> g, source) :> _
    interface IAsyncEnumerable<'U> with
      member __.GetEnumerator () =
        let mutable st = 0
        let mutable en = Unchecked.defaultof<_>
        { new IAsyncEnumerator<'U> with
            member __.MoveNext () = async {
              match st with
              | 0 -> 
                en <- source.GetEnumerator()
                st <- 1                
                return! __.MoveNext ()
              | _ ->
                let! next = en.MoveNext ()
                match next with
                | Some a -> return Some (f a)
                | None -> return None }
            member __.Dispose () =
              match st with
              | 1 -> en.Dispose ()
              | _ -> () }

  and MapAsyncEnumerable<'T, 'U> (f:'T -> Async<'U>, source:AsyncSeq<'T>) =
    inherit AsyncSeqOp<'U> () with
    override __.FoldAsync (g:'S -> 'U -> Async<'S>) (init:'S) = async {
      //printfn "MapAsyncEnumerable.FuseFoldAsync"
      use en = source.GetEnumerator ()
      let rec go s = async {
        let! next = en.MoveNext ()
        match next with
        | Some t ->
          let! u = f t
          let! s' = g s u
          return! go s'
        | None ->
          return s }
      return! go init }      
    override __.ChooseAsync (g:'U -> Async<'V option>) : AsyncSeq<'V> =
      let f () t = async {
        let! u = f t
        let! res' = g u
        match res' with
        | Some v ->
          return Some (v, ())
        | None ->
          return None }
      new ChooseStateAsyncEnumerable<unit, 'T, 'V> (f, (), source) :> _
    override  __.Map (g:'U -> 'V) : AsyncSeq<'V> = 
      let f t = async {
        let! u = f t
        return g u }
      new MapAsyncEnumerable<'T, 'V> (f, source) :> _          
    override  __.MapAsync (g:'U -> Async<'V>) : AsyncSeq<'V> = 
      let f t = async {
        let! u = f t
        return! g u }
      new MapAsyncEnumerable<'T, 'V> (f, source) :> _    
    interface IAsyncEnumerable<'U> with
      member __.GetEnumerator () =
        let mutable st = 0
        let mutable en = Unchecked.defaultof<_>
        { new IAsyncEnumerator<'U> with
            member __.MoveNext () = async {
              match st with
              | 0 -> 
                en <- source.GetEnumerator()
                st <- 1                
                return! __.MoveNext ()
              | _ ->
                let! next = en.MoveNext ()
                match next with
                | Some a -> 
                  let! b = f a
                  return Some b
                | None -> return None }
            member __.Dispose () =
              match st with
              | 1 -> en.Dispose ()
              | _ -> () }

  let unfoldAsync2 (f:'State -> Async<('T * 'State) option>) (s:'State) : AsyncSeq<'T> = 
    new UnfoldAsyncEnumerator<_, _>(f, s, Disposable.empty) :> _

  let unfoldDisposeAsync (f:'State -> Async<('T * 'State) option>) (s:'State) (d:IDisposable) : AsyncSeq<'T> = 
    new UnfoldAsyncEnumerator<_, _>(f, s, d) :> _
  
  let chooseStateAsync (f:'S -> 'T -> Async<('U * 'S) option>) (init:'S) (source:AsyncSeq<'T>) : AsyncSeq<'U> =
    new ChooseStateAsyncEnumerable<'S, 'T, 'U> (f, init, source) :> _
    
  let chooseState (f:'S -> 'T -> ('U * 'S) option) (init:'S) (source:AsyncSeq<'T>) : AsyncSeq<'U> =
    chooseStateAsync (fun s t -> f s t |> async.Return) init source

  let take2 (count:int) source = 
    let f i t =
      if i = count then None
      else Some (t, i + 1)
    chooseState f 0 source

  let chooseAsync3 f (source:AsyncSeq<'T>) =
    match source with
    | :? AsyncSeqOp<'T> as source -> source.ChooseAsync f
    | _ ->
      let f () = f >> Async.map (Option.map (fun u -> u, ()))
      chooseStateAsync f () source

  let mapAsync3 (f:'T -> Async<'U>) (source:AsyncSeq<'T>) : AsyncSeq<'U> =
    match source with
    | :? AsyncSeqOp<'T> as source -> source.MapAsync f
    | _ -> new MapAsyncEnumerable<_, _>(f, source) :> _
                           
  let map3 (f:'T -> 'U) (source:AsyncSeq<'T>) : AsyncSeq<'U> =
    match source with
    | :? AsyncSeqOp<'T> as source -> source.Map f
    | _ -> new MapEnumerable<_, _>(f, source) :> _

  let foldAsync3 f i (s:AsyncSeq<'T>) : Async<'U> =
    match s with 
    | :? AsyncSeqOp<'T> as source -> source.FoldAsync f i
    | _ -> async {
      use en = s.GetEnumerator ()
      let fin = ref false
      let init = ref i
      while not !fin do
        let! next = en.MoveNext ()
        match next with
        | Some t ->
          let! i = f !init t
          init := i 
        | None ->
          fin := true
      return !init }

  let fold3 f i (s:AsyncSeq<'T>) : Async<'U> =
    foldAsync3 (fun s t -> f s t |> async.Return) i s

  let inline sum3 (s:AsyncSeq<'a>) : Async<'a> =
    fold3 (+) LanguagePrimitives.GenericZero s

  let inline length2 (s:AsyncSeq<'a>) : Async<int> =
    fold3 (fun l _ -> l + 1) 0 s

  let iterAsync2 f (source:AsyncSeq<'T>) =
    match source with
    | :? AsyncSeqOp<'T> as source -> source.IterAsync f
    | _ -> AsyncSeq.iterAsync f source
  
  let iter3 f (source:AsyncSeq<'T>) : Async<unit> = 
    match source with
    | :? AsyncSeqOp<'T> as source -> source.Iter f
    | _ ->
      async {
        use en = source.GetEnumerator()
        let rec go () = async {
          let! next = en.MoveNext()
          match next with
          | None -> return ()
          | Some a ->
            do f a
            return! go () }
        return! go () }

  let skip2 (count:int) (source:AsyncSeq<'T>) : AsyncSeq<'T> =
    let en = source.GetEnumerator ()
    let rec go (en:IAsyncEnumerator<'T>, n:int) = async {
      let! next = en.MoveNext ()
      match next with
      | None -> return None
      | Some a ->
        if n > count then return Some (a, (en, n + 1))
        else return! go (en, n + 1) }
    unfoldDisposeAsync go (en, 0) (en :> IDisposable)
                    
  let foldRightAsync (f:'a -> Async<'b> -> Async<'b>) (acc:Async<'b>) (source:AsyncSeq<'a>) : Async<'b> = async {
    use en = source.GetEnumerator ()
    let rec go (acc:Async<'b>) = async {
      let! next = en.MoveNext ()
      match next with
      | None -> return! acc
      | Some a ->
        let! b' = f a (go acc)
        return b' }
    return! go acc }

  let tryFirst (s:AsyncSeq<'a>) : Async<'a option> =
    foldRightAsync (fun a _ -> async.Return (Some a)) (async.Return None) s

  let halt<'a, 'b> : Async<AsyncSeqTran<'a, 'b>> =
    async.Return Halt

  let await (cont:'a option -> Async<AsyncSeqTran<'a, 'b>>) : Async<AsyncSeqTran<'a, 'b>> =
    async.Return (Await cont)

  let awaitHalt (cont:'a -> Async<AsyncSeqTran<'a, 'b>>) : Async<AsyncSeqTran<'a, 'b>> =
    await (function Some a -> cont a | None -> halt)

  let emit (b:'b) (cont:Async<AsyncSeqTran<'a, 'b>>) : Async<AsyncSeqTran<'a, 'b>> =
    async.Return (Emit (b, cont))
      
  let repeat (tran:Async<AsyncSeqTran<'a, 'b>>) : Async<AsyncSeqTran<'a, 'b>> =
    let rec go t = async {
      let! t = t
      match t with
      | Halt -> return! go tran
      | Await rcv -> return! await (rcv >> go)
      | Emit (b,cont) -> return! emit b (go cont) }
    go tran

  let rec id<'a> : Async<AsyncSeqTran<'a, 'a>> = 
    awaitHalt (fun a -> emit a id)

  let skip3 (count:int) : AsyncSeq<'a> -> AsyncSeq<'a> =    
    let rec tran i =
      if i >= count then id
      else await (fun _ -> tran (i + 1))    
    AsyncSeqTran.apply (tran 0)

  let skip4 (count:int) : AsyncSeq<'a> -> AsyncSeq<'a> =    
    let rec tran i =
      if i >= count then id
      else await (fun _ -> tran (i + 1))    
    fun s -> 
      match s with
      | :? AsyncSeqOp<_> as x -> x.Apply (tran 0)
      | _ -> AsyncSeqTran.apply (tran 0) s
           
  let wye (s1:AsyncSeq<'a>) (s2:AsyncSeq<'b>) (t:Async<AsyncSeqTran<Choice<'a, 'b>, 'c>>) : AsyncSeq<'c> =      
    AsyncSeq.mergeChoice s1 s2 |> AsyncSeqTran.apply t
    
  let impulseByCountAndTime (count:int) (time:TimeSpan) : AsyncSeq<'a> -> AsyncSeq<'a[]> =
    let now () = Diagnostics.Stopwatch.GetTimestamp ()
    let rec init c t acc =
      let t' = now ()
      let delta = TimeSpan.FromTicks (t' - t)
      if delta < time then
        awaitHalt (fun a -> init (c + 1) t' (a::acc))
      else
        if c >= count then
          emit (acc |> List.toArray) (init c t [])
        else
          // TODO: trim
          awaitHalt (fun a -> init (c + 1) t' (a::acc))
    AsyncSeqTran.apply (init 0 (now ()) [])

  let impulseByCountAndTime2 (count:int) (time:TimeSpan) : AsyncSeq<'a> -> AsyncSeq<'a[]> =
    AsyncSeq.map (fun a -> Diagnostics.Stopwatch.GetTimestamp (), a)
    >> AsyncSeq.bufferByCount count
    >> AsyncSeq.choose (fun xs ->
      let t = fst xs.[0]
      let t'= fst xs.[xs.Length - 1]
      let d = TimeSpan.FromTicks (t' - t)
      if d >= time then Some (xs |> Array.map snd)
      else None)
                     

  // ------------------------------------------------------------------------------------


  let map = map3
  let unfoldAsync = unfoldAsync2
  let sum = sum3
  let take = take2
  let skip = skip2
  let iter = iter3
  let iterAsync = iterAsync2
  let chooseAsync = chooseAsync3

  // ------------------------------------------------------------------------------------
    


//let N = 10000000
let N = 5000000

let generator state = async {
  //printfn "gen=%A" state
  if state < N then
      return Some (state, state + 1)
  else
      return None }


//AsyncSeq.unfoldAsync generator 0
//|> AsyncSeq.skip (N / 2)
//|> AsyncSeq.take (N / 2)
//|> AsyncSeq.map id
//|> AsyncSeq.chooseAsync (Some >> async.Return)
//|> AsyncSeq.iter ignore
//|> Async.RunSynchronously

//AsyncSeq.unfoldAsync generator 0
////|> AsyncSeq.map id
//|> AsyncSeq.chooseAsync (Some >> async.Return)
//|> AsyncSeq.iterAsync (ignore >> async.Return)
//|> Async.RunSynchronously


AsyncSeq.unfoldAsync generator 0
|> AsyncSeq.skip3 N
|> AsyncSeq.skip4 N
//|> AsyncSeq.skip N
|> AsyncSeq.iter ignore
|> Async.RunSynchronously

//Real: 00:00:02.720, CPU: 00:00:02.718, GC gen0: 371, gen1: 2, gen2: 0




