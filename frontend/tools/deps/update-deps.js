const { rm } = require('node:fs/promises');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const rootDir = path.resolve(__dirname, '..', '..');
const cleanTargets = ['.angular', 'node_modules', 'package-lock.json'];

async function removeTarget(relativePath) {
  const targetPath = path.resolve(rootDir, relativePath);
  if (!targetPath.startsWith(`${rootDir}${path.sep}`)) {
    throw new Error(`Refusing to remove path outside project root: ${targetPath}`);
  }

  console.log(`Removing ${relativePath}`);
  await rm(targetPath, { recursive: true, force: true });
}

function run(command, args) {
  const isWindows = process.platform === 'win32';
  const executable = isWindows ? process.env.ComSpec || 'cmd.exe' : command;
  // Windows .cmd shims need a shell; callers supply only fixed, shell-safe arguments.
  const commandArgs = isWindows ? ['/d', '/s', '/c', `${command}.cmd`, ...args] : args;
  const result = spawnSync(executable, commandArgs, {
    cwd: rootDir,
    stdio: 'inherit',
  });

  if (result.error) {
    throw result.error;
  }

  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

async function main() {
  run('npx', ['--yes', 'npm-check-updates@latest', '--target', 'latest', '--peer', '--upgrade']);

  await Promise.all(cleanTargets.map(removeTarget));

  run('npm', ['install']);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
