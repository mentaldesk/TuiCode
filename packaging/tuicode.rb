class Tuicode < Formula
  desc "Minimalist terminal code editor for working over SSH"
  homepage "https://github.com/mentaldesk/TuiCode"
  license "MIT"

  on_macos do
    on_arm do
      url "https://github.com/mentaldesk/TuiCode/releases/download/v{{version}}/tuicode-{{version}}-osx-arm64.tar.gz"
      sha256 "{{sha_osx_arm64}}"
    end
  end

  on_linux do
    on_intel do
      url "https://github.com/mentaldesk/TuiCode/releases/download/v{{version}}/tuicode-{{version}}-linux-x64.tar.gz"
      sha256 "{{sha_linux_x64}}"
    end
    on_arm do
      url "https://github.com/mentaldesk/TuiCode/releases/download/v{{version}}/tuicode-{{version}}-linux-arm64.tar.gz"
      sha256 "{{sha_linux_arm64}}"
    end
  end

  def install
    bin.install "TuiCode" => "tuicode"
  end

  def caveats
    return unless OS.mac?

    <<~EOS
      To enable native macOS shortcuts in iTerm2 (Cmd+C/V/X/Z/A, Cmd+arrows,
      Shift+Cmd+arrows), run:

        tuicode --install-terminal-integration

      Or open Settings (Ctrl+,) → Terminal Integration from inside the editor.
      Other terminals will be added as they're supported — see:
        https://github.com/mentaldesk/TuiCode/issues/40
    EOS
  end

  test do
    assert_predicate bin/"tuicode", :executable?
  end
end
