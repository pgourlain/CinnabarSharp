using System;
using System.Threading.Tasks;
using CinnabarSharp.Core.Vector;
using CommunityToolkit.Mvvm.Input;

namespace CinnabarSharp.Desktop.ViewModels;

// Object menu (flip, rotate, align, distribute) and Path menu of SVG drawings.
public partial class MainViewModel
{
    private AlignRelativeTo _alignRelativeTo = AlignRelativeTo.FirstSelected;

    /// <summary>What Align lines the objects up with.</summary>
    public AlignRelativeTo AlignRelativeTo
    {
        get => _alignRelativeTo;
        set
        {
            _alignRelativeTo = value;
            OnPropertyChanged();
        }
    }

    private void NotifyObjectCommands()
    {
        foreach (var command in new IRelayCommand[]
                 {
                     DeselectObjectsCommand, DeleteObjectsCommand, DuplicateObjectsCommand, GroupObjectsCommand, UngroupObjectsCommand,
                     RaiseObjectsCommand, LowerObjectsCommand, RaiseToTopCommand, LowerToBottomCommand,
                     FlipObjectsHorizontalCommand, FlipObjectsVerticalCommand, RotateObjectsClockwiseCommand,
                     RotateObjectsCounterClockwiseCommand, AlignObjectsCommand, DistributeObjectsCommand, ObjectToPathCommand,
                     StrokeToPathCommand, ApplyPathOperationCommand, BreakApartPathsCommand, SimplifyPathsCommand, ReversePathsCommand,
                 })
            command.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SetAlignRelativeTo(AlignRelativeTo value) => AlignRelativeTo = value;

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void FlipObjectsHorizontal() => ActiveSvg?.Actions.Flip(null, horizontal: true);

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void FlipObjectsVertical() => ActiveSvg?.Actions.Flip(null, horizontal: false);

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void RotateObjectsClockwise() => ActiveSvg?.Actions.Rotate90(null, clockwise: true);

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void RotateObjectsCounterClockwise() => ActiveSvg?.Actions.Rotate90(null, clockwise: false);

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void AlignObjects(AlignEdge edge) => ActiveSvg?.Actions.Align(null, edge, AlignRelativeTo);

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void DistributeObjects(DistributeMode mode) => ActiveSvg?.Actions.Distribute(null, mode);

    private async Task PathEditAsync(Action<SvgDocument> edit)
    {
        if (ActiveSvg is not { } drawing)
            return;
        try
        {
            edit(drawing);
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            await (Dialogs?.ShowErrorAsync("Could not change the path", e.Message) ?? Task.CompletedTask);
        }
    }

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private Task ObjectToPath() => PathEditAsync(d => d.Actions.ObjectToPath());

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private Task StrokeToPath() => PathEditAsync(d => d.Actions.StrokeToPath());

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private Task ApplyPathOperation(PathOperation operation) => PathEditAsync(d => d.Actions.ApplyPathOperation(operation));

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private Task BreakApartPaths() => PathEditAsync(d => d.Actions.BreakApart());

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private Task SimplifyPaths() => PathEditAsync(d => d.Actions.Simplify());

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private Task ReversePaths() => PathEditAsync(d => d.Actions.Reverse());
}
