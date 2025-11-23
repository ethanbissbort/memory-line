const path = require('path');
const webpack = require('webpack');
const HtmlWebpackPlugin = require('html-webpack-plugin');

module.exports = {
    mode: process.env.NODE_ENV === 'production' ? 'production' : 'development',
    entry: './src/renderer/index.jsx',
    target: 'electron-renderer',
    devtool: 'source-map',
    output: {
        path: path.resolve(__dirname, 'dist'),
        filename: 'renderer.js'
    },
    resolve: {
        extensions: ['.js', '.jsx', '.json'],
        fallback: {
            "buffer": require.resolve("buffer/"),
            "process": require.resolve("process/browser")
        }
    },
    module: {
        rules: [
            {
                test: /\.jsx?$/,
                exclude: /node_modules/,
                use: {
                    loader: 'babel-loader',
                    options: {
                        presets: ['@babel/preset-env', '@babel/preset-react']
                    }
                }
            },
            {
                test: /\.css$/,
                use: ['style-loader', 'css-loader']
            }
        ]
    },
    plugins: [
        new HtmlWebpackPlugin({
            template: './public/index.html',
            filename: 'index.html'
        }),
        new webpack.ProvidePlugin({
            Buffer: ['buffer', 'Buffer'],
            process: 'process/browser'
        }),
        new webpack.DefinePlugin({
            'global': 'globalThis'
        })
    ],
    devServer: {
        static: {
            directory: path.join(__dirname, 'dist')
        },
        compress: true,
        port: 8080,
        hot: true
    }
};
