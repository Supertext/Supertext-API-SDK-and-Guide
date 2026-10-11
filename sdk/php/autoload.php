<?php

declare(strict_types=1);

/*
 * Autoloader for using the SDK without Composer (tests, the conformance program, copying the folder
 * into a project). With Composer, use vendor/autoload.php instead.
 */
spl_autoload_register(static function (string $class): void {
    foreach (['Supertext\\Sdk\\Tests\\' => __DIR__ . '/tests/', 'Supertext\\Sdk\\' => __DIR__ . '/src/'] as $prefix => $dir) {
        if (str_starts_with($class, $prefix)) {
            $file = $dir . str_replace('\\', '/', substr($class, \strlen($prefix))) . '.php';
            if (is_file($file)) {
                require $file;
            }

            return;
        }
    }
});
