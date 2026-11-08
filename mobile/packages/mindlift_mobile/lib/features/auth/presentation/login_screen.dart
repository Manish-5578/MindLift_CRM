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
  late final TextEditingController _serverController;
  bool _showServerSettings = false;

  @override
  void initState() {
    super.initState();
    _serverController = TextEditingController(text: ref.read(serverUrlProvider));
  }

  @override
  void dispose() {
    _emailController.dispose();
    _passwordController.dispose();
    _serverController.dispose();
    super.dispose();
  }

  Future<void> _handleLogin() async {
    final email = _emailController.text.trim();
    final password = _passwordController.text;

    if (email.isEmpty || password.isEmpty) {
      MlToast.show(context, message: 'Please enter both email and password.');
      return;
    }

    final success = await ref.read(authNotifierProvider.notifier).login(email, password);
    if (!success && mounted) {
      final err = ref.read(authNotifierProvider).errorMessage ?? 'Login failed.';
      MlToast.show(context, message: err, backgroundColor: MlColors.danger);
    }
  }

  void _applyServerUrl(String url) {
    ref.read(serverUrlProvider.notifier).setUrl(url);
    _serverController.text = url;
    MlToast.show(context, message: 'Server URL updated to $url', type: MlToastType.success);
  }

  @override
  Widget build(BuildContext context) {
    final authState = ref.watch(authNotifierProvider);
    final currentServerUrl = ref.watch(serverUrlProvider);

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
                  BoxShadow(
                    color: Color(0x0F0F172A),
                    blurRadius: 24,
                    offset: Offset(0, 8),
                  ),
                ],
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                mainAxisSize: MainAxisSize.min,
                children: [
                  // App Brand Logo / Title
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
                          style: TextStyle(
                            fontSize: 24,
                            fontWeight: FontWeight.w900,
                            color: MlColors.white,
                          ),
                        ),
                      ),
                      const SizedBox(width: MlSpacing.sm),
                      Text(
                        'MindLift CRM',
                        style: MlTypography.headingMedium.copyWith(color: MlColors.textPrimary),
                      ),
                    ],
                  ),
                  const SizedBox(height: MlSpacing.sm),
                  Text(
                    'Staff Mobile Portal',
                    style: MlTypography.bodyMedium.copyWith(color: MlColors.textMuted),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: MlSpacing.lg),

                  // Error Banner if login fails
                  if (authState.errorMessage != null) ...[
                    Container(
                      padding: const EdgeInsets.all(MlSpacing.md),
                      decoration: BoxDecoration(
                        color: MlColors.dangerSoft,
                        borderRadius: BorderRadius.circular(MlRadii.md),
                        border: Border.all(color: const Color(0xFFFCA5A5)),
                      ),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const Text('⚠️ ', style: TextStyle(fontSize: 16)),
                          Expanded(
                            child: Text(
                              authState.errorMessage!,
                              style: MlTypography.caption.copyWith(
                                color: const Color(0xFF991B1B),
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: MlSpacing.md),
                  ],

                  // Email input
                  MlTextField(
                    label: 'Work Email',
                    placeholder: 'user@mindlift.com',
                    controller: _emailController,
                  ),
                  const SizedBox(height: MlSpacing.md),

                  // Password input
                  MlTextField(
                    label: 'Password',
                    placeholder: '••••••••••••',
                    controller: _passwordController,
                    obscureText: true,
                  ),
                  const SizedBox(height: MlSpacing.lg),

                  // Submit button
                  MlButton(
                    label: 'Sign In to Workspace',
                    onPressed: authState.isLoading ? null : _handleLogin,
                    isLoading: authState.isLoading,
                    variant: MlButtonVariant.primary,
                  ),
                  const SizedBox(height: MlSpacing.lg),

                  // Collapsible Server Configuration
                  GestureDetector(
                    onTap: () => setState(() => _showServerSettings = !_showServerSettings),
                    child: Row(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Text(
                          _showServerSettings ? '▲ Hide Server Settings' : '⚙️ Server: $currentServerUrl',
                          style: MlTypography.caption.copyWith(
                            color: MlColors.primary,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ],
                    ),
                  ),

                  if (_showServerSettings) ...[
                    const SizedBox(height: MlSpacing.md),
                    Container(
                      padding: const EdgeInsets.all(MlSpacing.md),
                      decoration: BoxDecoration(
                        color: MlColors.surfaceElevated,
                        borderRadius: BorderRadius.circular(MlRadii.md),
                        border: Border.all(color: MlColors.border),
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Text(
                            'Backend Server API URL:',
                            style: MlTypography.caption.copyWith(
                              color: MlColors.textSecondary,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                          const SizedBox(height: MlSpacing.xs),
                          MlTextField(
                            controller: _serverController,
                            placeholder: 'https://mindlift-staging.onrender.com',
                          ),
                          const SizedBox(height: MlSpacing.sm),
                          Row(
                            children: [
                              Expanded(
                                child: MlButton(
                                  label: 'Set URL',
                                  variant: MlButtonVariant.secondary,
                                  onPressed: () => _applyServerUrl(_serverController.text),
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: MlSpacing.sm),
                          Wrap(
                            spacing: MlSpacing.md,
                            runSpacing: MlSpacing.xs,
                            children: [
                              GestureDetector(
                                onTap: () => _applyServerUrl('https://mindlift-staging.onrender.com'),
                                child: Text('🌐 Staging (Cloud)', style: MlTypography.caption.copyWith(color: MlColors.primary, fontWeight: FontWeight.w600)),
                              ),
                              GestureDetector(
                                onTap: () => _applyServerUrl('http://10.0.2.2:5082'),
                                child: Text('📱 Emulator (10.0.2.2)', style: MlTypography.caption.copyWith(color: MlColors.textSecondary)),
                              ),
                              GestureDetector(
                                onTap: () => _applyServerUrl('http://localhost:5082'),
                                child: Text('💻 Localhost', style: MlTypography.caption.copyWith(color: MlColors.textSecondary)),
                              ),
                            ],
                          ),
                        ],
                      ),
                    ),
                  ],

                  const SizedBox(height: MlSpacing.md),
                  Text(
                    'Field agents only. Session is securely encrypted.',
                    style: MlTypography.caption.copyWith(color: MlColors.textMuted),
                    textAlign: TextAlign.center,
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
