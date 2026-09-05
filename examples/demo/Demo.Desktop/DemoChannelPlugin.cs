using Tarui.Contracts;
using Tarui.Ipc;

namespace Demo;

/// <summary>
/// Demonstrates the end-to-end Channel streaming IPC surface: the front-end supplies a
/// <see cref="TaruiChannel{T}"/> argument and this handler pushes an incremental progress frame for each step.
/// Every <see cref="TaruiChannel{T}.SendAsync"/> call is routed back to the invoking web view's
/// <see cref="IChannelSink"/> and delivered to the front-end channel's <c>onmessage</c>.
/// </summary>
public sealed class DemoChannelPlugin : ITaruiPlugin
{
    public const string StreamEchoCommand = "core:channel|stream-echo";

    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            StreamEchoCommand,
            TaruiJsonContext.Default.StreamEchoArgs,
            TaruiJsonContext.Default.Unit,
            Handler,
            StreamEchoCommand);
    }

    private static async ValueTask<Unit> Handler(
        StreamEchoArgs args,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        // Bind the plain channel token to a typed channel routed back to the invoking webview.
        var channel = ChannelContext.Bind<StreamProgress>(args.Channel);
        var count = args.Count > 0 ? args.Count : 0;
        for (var step = 0; step < count && !cancellationToken.IsCancellationRequested; step++)
        {
            await channel.SendAsync(new StreamProgress(step, count), cancellationToken);
        }

        return new Unit();
    }
}