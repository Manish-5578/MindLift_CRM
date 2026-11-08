import 'package:flutter/services.dart' show TextInputAction;
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mindlift_ui/mindlift_ui.dart';
import '../../../core/network_provider.dart';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();

  @override
  void dispose() {
    _emailController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  Future<void> _handleLogin() async {
    final email = _emailController.text.trim();
    final password = _passwordController.text;

    if (email.isEmpty || password.isEmpty) {
      MlToast.show(context, message: 'Please enter your email and password.');
      return;
    }

    final success = await ref.read(authNotifierProvider.notifier).login(email, password);
    if (!success && mounted) {
      final err = ref.read(authNotifierProvider).errorMessage ?? 'Login failed.';
      MlToast.show(context, message: err, backgroundColor: MlColors.danger);
    }
  }

  @override
  Widget build(BuildContext context) {
    final authState = ref.watch(authNotifierProvider);

    return MlScreen(
      backgroundColor: MlColors.background,
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(MlSpacing.xl),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 440),
            child: Container(
              padding: const EdgeInsets.all(MlSpacing.xl),
              decoration: BoxDecoration(
                color: MlColors.surface,
                borderRadius: BorderRadius.circular(MlRadii.xl),
                border: Border.all(color: MlColors.border),
                boxShadow: const [
                  BoxShadow(color: Color(0x0F0F172A), blurRadius: 24, offset: Offset(0, 8)),
                ],
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Container(
                        width: 44,
                        height: 44,
                        decoration: BoxDecoration(
                          color: MlColors.primary,
                          borderRadius: BorderRadius.circular(MlRadii.md),
                        ),
                        alignment: Alignment.center,
                        child: const Text(
                          'M',
                          style: TextStyle(fontSize: 24, fontWeight: FontWeight.w900, color: Color(0xFFFFFFFF)),
                        ),
                      ),
                      const SizedBox(width: MlSpacing.sm),
                      Text(
                        'MindLift CRM',
                        style: MlTypography.headingMedium.copyWith(color: MlColors.textPrimary),
                      ),
                    ],
                  ),
                  const SizedBox(height: MlSpacing.xl),
                  if (authState.errorMessage != null) ...[
                    Container(
                      padding: const EdgeInsets.all(MlSpacing.md),
                      decoration: BoxDecoration(
                        color: MlColors.dangerSoft,
                        borderRadius: BorderRadius.circular(MlRadii.md),
                        border: Border.all(color: MlColors.danger),
                      ),
                      child: Text(
                        authState.errorMessage!,
                        style: MlTypography.caption.copyWith(color: MlColors.danger, fontWeight: FontWeight.w600),
                      ),
                    ),
                    const SizedBox(height: MlSpacing.md),
                  ],
                  MlTextField(
                    label: 'Email',
                    placeholder: 'name@company.com',
                    controller: _emailController,
                    keyboardType: TextInputType.emailAddress,
                    textInputAction: TextInputAction.next,
                  ),
                  const SizedBox(height: MlSpacing.md),
                  MlTextField(
                    label: 'Password',
                    placeholder: '••••••••',
                    controller: _passwordController,
                    obscureText: true,
                    textInputAction: TextInputAction.done,
                    onSubmitted: (_) => _handleLogin(),
                  ),
                  const SizedBox(height: MlSpacing.lg),
                  MlButton(
                    label: 'Sign In',
                    onPressed: authState.isLoading ? null : _handleLogin,
                    isLoading: authState.isLoading,
                    variant: MlButtonVariant.primary,
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
