open System
open System.Reflection
open System.IO

let dll = @"c:\Users\hiyan\.nuget\packages\avalonia\12.1.1\lib\net10.0\Avalonia.Controls.dll"
let asm = Assembly.LoadFrom dll

let types = asm.GetTypes()

let show (n: string) =
    let t = types |> Array.tryFind (fun x -> x.Name = n) 
    match t with
    | None -> printfn "=== '%s' NOT FOUND ===" n
    | Some ty ->
        printfn "=== %s (IsEnum=%b) ===" n (ty.IsEnum)
        if ty.IsEnum then
            Enum.GetNames ty |> Array.iter (printfn "   enum: %s")
        else
            let ctors =
                ty.GetConstructors(BindingFlags.Public ||| BindingFlags.Instance ||| BindingFlags.DeclaredOnly)
                |> Array.map (fun c -> "ctor(" + (c.GetParameters() |> Array.map (fun p -> p.ParameterType.Name) |> String.concat ", ") + ")")
            if ctors.Length > 0 then ctors |> Array.iter (printfn "   %s")
            ty.GetProperties(BindingFlags.Public ||| BindingFlags.Instance ||| BindingFlags.DeclaredOnly ||| BindingFlags.Static)
            |> Array.map (fun p -> sprintf "property %s : %s" p.Name p.PropertyType.Name)
            |> Array.iter (printfn "   %s")
            ty.GetMethods(BindingFlags.Public ||| BindingFlags.Instance ||| BindingFlags.DeclaredOnly ||| BindingFlags.Static)
            |> Array.map (fun m -> sprintf "method %s(%s)" m.Name (m.GetParameters() |> Array.map (fun p -> p.ParameterType.Name) |> String.concat ", "))
            |> Array.iter (printfn "   %s")

[ "NativeMenu"; "NativeMenuItem"; "NativeMenuItemSeparator"; "NativeMenuItemToggleType"; "TrayIcon"; "WindowIcon"; "Window" ]
|> List.iter show