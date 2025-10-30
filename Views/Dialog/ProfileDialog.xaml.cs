using System.ComponentModel;
using System.Windows;

namespace DeepDenoiseClient.Views.Dialog
{
    public partial class ProfileDialog : Window
    {
        public ProfileDialog()
        {
            InitializeComponent();

            // DataContext가 이미 할당되어 있으면 바로 연결
            if (DataContext is ProfileDialogViewModel vm)
            {
                vm.PropertyChanged += Vm_PropertyChanged;
            }
            // DataContext가 나중에 할당될 수도 있으니 DataContextChanged도 구독
            this.DataContextChanged += (s, e) =>
            {
                if (e.NewValue is ProfileDialogViewModel newVm)
                {
                    newVm.PropertyChanged += Vm_PropertyChanged;
                }
            };
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is ProfileDialogViewModel vm && e.PropertyName == nameof(ProfileDialogViewModel.DialogResult) && vm.DialogResult.HasValue)
            {
                this.DialogResult = vm.DialogResult;
            }
        }
    }
}